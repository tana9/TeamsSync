using System.Globalization;
using System.Net;

using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

using TeamsSync.Domain.Teams;

using TeamInfo = TeamsSync.Domain.Teams.TeamInfo;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
///     複数チームのメンバー一覧をGraphの$batchでまとめて取得し、429/503応答は
///     待機のうえ再試行する。バッチ送信とスロットリング再試行のみを担当し、
///     再試行上限に達してもなお取得できなかった項目は呼び出し元の個別フォールバックに委ねる
/// </summary>
public sealed class TeamMembersBatchFetcher(GraphSdkClient sdk, ILogger logger)
{
    // バッチ内429/503の再試行上限。無限ループを避けつつ、一時的なスロットリングから回復する猶予を与える
    private const int MaxBatchAttempts = 3;

    // バッチ項目にRetry-Afterがない場合の既定待機時間。$batch自体のHTTPリトライ(DependencyInjection.csの
    // AddStandardResilienceHandler)とは別に、バッチ項目単位で待つための保守的な初期値
    private static readonly TimeSpan DefaultThrottleRetryDelay = TimeSpan.FromSeconds(2);

    // - 429/503(Graph側の一時的なスロットリング・過負荷): Retry-Afterに従い待機したうえで、
    //   失敗した項目だけを再度バッチ送信する。待たずに個別APIへ切り替えるとGraphへの負荷を
    //   むしろ増幅してしまうため、ここでは即時フォールバックしない。
    // - それ以外(403・400など権限不足や不正要求): 待っても解決しないため、結果に含めず
    //   呼び出し元の個別フォールバックに委ねる。
    // - 再試行上限(MaxBatchAttempts)に達してもなお429/503の項目も同様に結果へ含めない
    /// <summary>
    ///     バッチリクエストを送信し、429/503応答は待機のうえ再試行する。403/400等の応答や
    ///     再試行上限に達した項目は結果に含めない
    /// </summary>
    public async Task<Dictionary<int, List<TeamMember>>> FetchAsync(IReadOnlyList<TeamInfo> candidates,
        IReadOnlyList<int> batch, CancellationToken cancellationToken)
    {
        Dictionary<int, List<TeamMember>> membersByIndex = new();
        List<int> pending = batch.ToList();

        for (int attempt = 1; attempt <= MaxBatchAttempts && pending.Count > 0; attempt++)
        {
            bool isLastAttempt = attempt == MaxBatchAttempts;
            List<(string RequestId, string TeamId)> requests = pending.Select(index =>
                (RequestId: index.ToString(CultureInfo.InvariantCulture), TeamId: candidates[index].Id)).ToList();
            BatchResponseContentCollection responses = await sdk.SendTeamMembersBatchAsync(requests, cancellationToken);

            (List<int> retryable, TimeSpan? retryAfter) = await ClassifyResponsesAsync(
                candidates, pending, responses, membersByIndex, isLastAttempt, cancellationToken);
            pending = retryable;
            if (pending.Count > 0)
            {
                // 複数バッチが同時に429を受けても再試行が一斉に集中しないよう、Retry-Afterにジッターを加える
                TimeSpan delay = (retryAfter ?? DefaultThrottleRetryDelay) + JitterDelay();
                logger.LogWarning(
                    "バッチ内で{Count}件がスロットリング・過負荷応答のため{DelayMs}ms待機して再試行します。Attempt={Attempt}",
                    pending.Count, (int)delay.TotalMilliseconds, attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return membersByIndex;
    }

    /// <summary>
    ///     1回のバッチ応答を分類する。200応答はmembersByIndexへ積み上げ、429/503応答は
    ///     再試行対象として集める。それ以外(403/400等)はどちらにも含めず個別フォールバックへ委ねる
    /// </summary>
    private async Task<(List<int> Retryable, TimeSpan? RetryAfter)> ClassifyResponsesAsync(
        IReadOnlyList<TeamInfo> candidates, IReadOnlyList<int> pending, BatchResponseContentCollection responses,
        Dictionary<int, List<TeamMember>> membersByIndex, bool isLastAttempt, CancellationToken cancellationToken)
    {
        List<int> retryable = [];
        TimeSpan? retryAfter = null;
        Dictionary<string, HttpStatusCode> statusCodes = await responses.GetResponsesStatusCodesAsync();

        foreach (int index in pending)
        {
            string requestId = index.ToString(CultureInfo.InvariantCulture);
            if (!statusCodes.TryGetValue(requestId, out HttpStatusCode status))
            {
                continue; // membersByIndexに残らず、最後に個別フォールバックされる
            }

            if (status == HttpStatusCode.OK)
            {
                try
                {
                    membersByIndex[index] =
                        await ParseBatchMemberResponseAsync(responses, requestId, cancellationToken);
                }
                catch (InvalidDataException ex)
                {
                    // Graphが返すメンバー情報が想定外の形状(userId欠落等)の場合、このチームだけ
                    // membersByIndexに残さず個別フォールバックへ委ね、バッチ全体・他チームの
                    // 判定を継続する(403/400等と同じ扱い)
                    logger.LogWarning(ex,
                        "バッチ応答のメンバー情報を解析できませんでした。個別に再取得します。TeamId={TeamId}",
                        candidates[index].Id);
                }
            }
            else if (!isLastAttempt && status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                retryable.Add(index);
                using HttpResponseMessage rawResponse = await responses.GetResponseByIdAsync(requestId);
                TimeSpan? itemRetryAfter = rawResponse.Headers.RetryAfter?.Delta;
                if (itemRetryAfter is { } value && (retryAfter is null || value > retryAfter))
                {
                    retryAfter = value;
                }
            }
            // それ以外(403/400等、または再試行上限に達した429/503)はmembersByIndexに残さず
            // 個別フォールバックへ委ねる
        }

        return (retryable, retryAfter);
    }

    /// <summary>バッチ応答1件分のメンバー一覧を解析し、ページングが必要な場合は続きも取得する</summary>
    private async Task<List<TeamMember>> ParseBatchMemberResponseAsync(BatchResponseContentCollection responses,
        string requestId, CancellationToken cancellationToken)
    {
        ConversationMemberCollectionResponse? firstPage =
            await responses.GetResponseByIdAsync<ConversationMemberCollectionResponse>(requestId);
        IReadOnlyList<ConversationMember> members =
            await sdk.CollectTeamMembersPagesAsync(firstPage, cancellationToken);
        return GraphResponseParser.ParseTeamMembers(members);
    }

    /// <summary>再試行の集中を避けるための、0～500msのランダムなジッター遅延</summary>
    private static TimeSpan JitterDelay()
    {
        return TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
    }
}
