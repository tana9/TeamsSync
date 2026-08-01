using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
/// 複数チームのメンバー一覧をGraphの$batchでまとめて取得し、429/503応答は
/// 待機のうえ再試行する。バッチ送信とスロットリング再試行のみを担当し、
/// 再試行上限に達してもなお取得できなかった項目は呼び出し元の個別フォールバックに委ねる。
/// </summary>
internal sealed class TeamMembersBatchFetcher(GraphHttpClient http, ILogger logger)
{
    // バッチ内429/503の再試行上限。無限ループを避けつつ、一時的なスロットリングから回復する猶予を与える。
    private const int MaxBatchAttempts = 3;

    // バッチ項目にRetry-Afterがない場合の既定待機時間。$batch自体のHTTPリトライ(DependencyInjection.csの
    // AddStandardResilienceHandler)とは別に、バッチ項目単位で待つための保守的な初期値。
    private static readonly TimeSpan DefaultThrottleRetryDelay = TimeSpan.FromSeconds(2);

    // - 429/503(Graph側の一時的なスロットリング・過負荷): Retry-Afterに従い待機したうえで、
    //   失敗した項目だけを再度バッチ送信する。待たずに個別APIへ切り替えるとGraphへの負荷を
    //   むしろ増幅してしまうため、ここでは即時フォールバックしない。
    // - それ以外(403・400など権限不足や不正要求): 待っても解決しないため、結果に含めず
    //   呼び出し元の個別フォールバックに委ねる。
    // - 再試行上限(MaxBatchAttempts)に達してもなお429/503の項目も同様に結果へ含めない。
    /// <summary>
    /// バッチリクエストを送信し、429/503応答は待機のうえ再試行する。403/400等の応答や
    /// 再試行上限に達した項目は結果に含めない。
    /// </summary>
    public async Task<Dictionary<int, List<TeamMember>>> FetchAsync(IReadOnlyList<TeamInfo> candidates,
        IReadOnlyList<int> batch, CancellationToken cancellationToken)
    {
        var membersByIndex = new Dictionary<int, List<TeamMember>>();
        var pending = batch.ToList();

        for (var attempt = 1; attempt <= MaxBatchAttempts && pending.Count > 0; attempt++)
        {
            var isLastAttempt = attempt == MaxBatchAttempts;
            var requests = pending.Select(index => (
                Id: index.ToString(CultureInfo.InvariantCulture),
                Url: $"/teams/{candidates[index].Id}/members?$top=999")).ToList();
            var responses = await http.SendBatchAsync(requests, cancellationToken);

            List<int> retryable = [];
            TimeSpan? retryAfter = null;
            foreach (var index in pending)
            {
                if (!responses.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out var response))
                    continue; // membersByIndexに残らず、最後に個別フォールバックされる

                var status = response.GetProperty("status").GetInt32();
                if (status == 200)
                {
                    membersByIndex[index] = await ParseBatchMemberResponseAsync(response, cancellationToken);
                }
                else if (!isLastAttempt && status is 429 or 503)
                {
                    retryable.Add(index);
                    var itemRetryAfter = ParseRetryAfter(response);
                    if (itemRetryAfter is { } value && (retryAfter is null || value > retryAfter))
                        retryAfter = value;
                }
                // それ以外(403/400等、または再試行上限に達した429/503)はmembersByIndexに残さず
                // 個別フォールバックへ委ねる。
            }

            pending = retryable;
            if (pending.Count > 0)
            {
                // 複数バッチが同時に429を受けても再試行が一斉に集中しないよう、Retry-Afterにジッターを加える。
                var delay = (retryAfter ?? DefaultThrottleRetryDelay) + JitterDelay();
                logger.LogWarning(
                    "バッチ内で{Count}件がスロットリング・過負荷応答のため{DelayMs}ms待機して再試行します。Attempt={Attempt}",
                    pending.Count, (int)delay.TotalMilliseconds, attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return membersByIndex;
    }

    /// <summary>バッチ応答1件分のメンバー一覧を解析し、ページングが必要な場合は続きも取得する。</summary>
    private async Task<List<TeamMember>> ParseBatchMemberResponseAsync(JsonElement response,
        CancellationToken cancellationToken)
    {
        var body = response.GetProperty("body");
        var members = GraphResponseParser.ParseTeamMembers(body.GetProperty("value").EnumerateArray());
        if (body.TryGetProperty("@odata.nextLink", out var nextLink) &&
            nextLink.ValueKind == JsonValueKind.String)
        {
            var extra = await http.GetPagedAsync(nextLink.GetString()!, cancellationToken);
            members.AddRange(GraphResponseParser.ParseTeamMembers(extra));
        }

        return members;
    }

    // Graphの$batchレスポンスは項目ごとに{"headers":{"Retry-After":"10"}}のようにヘッダーを個別に持つ。
    // 秒数のdelta-seconds形式のみ対応(Graphの429/503はHTTP-date形式を返さないため十分)。
    /// <summary>バッチ応答項目のヘッダーからRetry-After(秒数)を解析する。</summary>
    private static TimeSpan? ParseRetryAfter(JsonElement response)
    {
        if (!response.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var header in headers.EnumerateObject())
        {
            if (!string.Equals(header.Name, "Retry-After", StringComparison.OrdinalIgnoreCase))
                continue;
            var text = header.Value.ValueKind switch
            {
                JsonValueKind.String => header.Value.GetString(),
                JsonValueKind.Number => header.Value.GetRawText(),
                _ => null
            };
            if (text is not null &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
                seconds >= 0)
                return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    /// <summary>再試行の集中を避けるための、0～500msのランダムなジッター遅延。</summary>
    private static TimeSpan JitterDelay() => TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
}
