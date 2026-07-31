using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
/// Microsoft Graph APIを介して、所有チームの判定、メンバー一覧の取得、
/// ユーザー検索、メンバーの追加・削除を行う。
/// </summary>
public sealed class GraphTeamsGateway(GraphHttpClient http, ILogger<GraphTeamsGateway> logger) : ITeamsGateway
{
    private const int OwnedTeamLookupConcurrency = 3;
    private const int OwnedTeamBatchSize = 20;

    // バッチ内429/503の再試行上限。無限ループを避けつつ、一時的なスロットリングから回復する猶予を与える。
    private const int MaxBatchAttempts = 3;

    // バッチ項目にRetry-Afterがない場合の既定待機時間。$batch自体のHTTPリトライ(DependencyInjection.csの
    // AddStandardResilienceHandler)とは別に、バッチ項目単位で待つための保守的な初期値。
    private static readonly TimeSpan DefaultThrottleRetryDelay = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<(string UserId, string TeamId), bool> _ownershipCache = new();

    /// <summary>サインイン中のユーザー自身の情報を取得する。</summary>
    public async Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default)
    {
        using var doc = await http.GetAsync("me?$select=id,displayName,userPrincipalName", cancellationToken);
        var root = doc.RootElement;
        return (Required(root, "id"), Required(root, "displayName"), Required(root, "userPrincipalName"));
    }

    /// <summary>
    /// サインイン中のユーザーが参加している全チームのうち、所有者になっているチームを判定して返す。
    /// キャッシュ済みの判定結果を再利用しつつ、未判定のチームはバッチ取得で解決する。
    /// </summary>
    public async Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
        CancellationToken cancellationToken = default)
    {
        // me/joinedTeamsは$topクエリオプションを許可しない(400 "Query option 'Top' is not allowed")ため、
        // 既定ページサイズのまま@odata.nextLinkでページングする。
        var teams = await http.GetPagedAsync("me/joinedTeams", cancellationToken);
        var candidates = teams.Select(x => new TeamInfo(
            Required(x, "id"), Required(x, "displayName"), Optional(x, "description"))).ToList();

        var stopwatch = Stopwatch.StartNew();
        var cacheHits = 0;
        var ownership = new bool[candidates.Count];
        var pendingIndexes = new List<int>();
        for (var i = 0; i < candidates.Count; i++)
        {
            if (_ownershipCache.TryGetValue((currentUserId, candidates[i].Id), out var cached))
            {
                cacheHits++;
                ownership[i] = cached;
            }
            else
            {
                pendingIndexes.Add(i);
            }
        }

        var batchCalls = 0;
        using var concurrency = new SemaphoreSlim(OwnedTeamLookupConcurrency);
        await Task.WhenAll(pendingIndexes.Chunk(OwnedTeamBatchSize).Select(async batch =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                Interlocked.Increment(ref batchCalls);
                await ResolveOwnershipBatchAsync(currentUserId, candidates, batch, ownership, cancellationToken);
            }
            finally
            {
                concurrency.Release();
            }
        }));

        var owned = candidates.Where((_, index) => ownership[index])
            .OrderBy(team => team.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        logger.LogInformation(
            "所有チームを取得しました。Owned={OwnedCount}, Candidates={CandidateCount}, CacheHits={CacheHits}, ElapsedMs={ElapsedMs}, BatchCalls={BatchCalls}",
            owned.Count, candidates.Count, cacheHits, stopwatch.ElapsedMilliseconds, batchCalls);
        return owned;
    }

    // バッチ内で失敗した項目を状況ごとに扱い分ける。
    // - 429/503(Graph側の一時的なスロットリング・過負荷): Retry-Afterに従い待機したうえで、
    //   失敗した項目だけを再度バッチ送信する。待たずに個別APIへ切り替えるとGraphへの負荷を
    //   むしろ増幅してしまうため、ここでは即時フォールバックしない。
    // - それ以外(403・400など権限不足や不正要求): 待っても解決しないため、従来どおり
    //   GetTeamMembersAsyncで個別に再取得する。
    // - 再試行上限(MaxBatchAttempts)に達してもなお429/503の項目は、最終手段として個別取得へ回す。
    /// <summary>
    /// 1バッチ分の候補チームについて、メンバー一覧をバッチ取得(失敗分は個別フォールバック)したうえで
    /// 所有者判定を行い、結果をキャッシュへ書き込む。
    /// </summary>
    private async Task ResolveOwnershipBatchAsync(string currentUserId, IReadOnlyList<TeamInfo> candidates,
        IReadOnlyList<int> batch, bool[] ownership, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var membersByIndex = await SendBatchWithRetryAsync(candidates, batch, cancellationToken);

        foreach (var index in batch)
        {
            var team = candidates[index];
            var members = membersByIndex.TryGetValue(index, out var resolved)
                ? resolved
                : await FetchMembersIndividuallyAsync(team, cancellationToken);

            var isOwner = members.Any(member =>
                member.IsOwner && string.Equals(member.UserId, currentUserId,
                    StringComparison.OrdinalIgnoreCase));
            _ownershipCache[(currentUserId, team.Id)] = isOwner;
            ownership[index] = isOwner;
        }
    }

    // バッチ送信とスロットリング再試行のみを担当し、最終的に取得できなかった項目は
    // 呼び出し元(ResolveOwnershipBatchAsync)の個別フォールバックに委ねる。
    /// <summary>
    /// バッチリクエストを送信し、429/503応答は待機のうえ再試行する。403/400等の応答や
    /// 再試行上限に達した項目は結果に含めない。
    /// </summary>
    private async Task<Dictionary<int, List<TeamMember>>> SendBatchWithRetryAsync(IReadOnlyList<TeamInfo> candidates,
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

            var retryable = new List<int>();
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

    /// <summary>バッチ取得できなかったチームのメンバー一覧を個別APIで再取得する。</summary>
    private async Task<List<TeamMember>> FetchMembersIndividuallyAsync(TeamInfo team,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "バッチ内のメンバー取得に失敗したため個別に再取得します。TeamId={TeamId}", team.Id);
        return (await GetTeamMembersAsync(team.Id, cancellationToken)).ToList();
    }

    /// <summary>バッチ応答1件分のメンバー一覧を解析し、ページングが必要な場合は続きも取得する。</summary>
    private async Task<List<TeamMember>> ParseBatchMemberResponseAsync(JsonElement response,
        CancellationToken cancellationToken)
    {
        var body = response.GetProperty("body");
        var members = ParseTeamMembers(body.GetProperty("value").EnumerateArray());
        if (body.TryGetProperty("@odata.nextLink", out var nextLink) &&
            nextLink.ValueKind == JsonValueKind.String)
        {
            var extra = await http.GetPagedAsync(nextLink.GetString()!, cancellationToken);
            members.AddRange(ParseTeamMembers(extra));
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

    /// <summary>所有チーム判定のキャッシュを消去する。ユーザーIDを指定するとそのユーザー分のみ消去する。</summary>
    public void ClearOwnedTeamsCache(string? currentUserId = null)
    {
        foreach (var key in _ownershipCache.Keys)
            if (currentUserId is null || string.Equals(
                    key.UserId, currentUserId, StringComparison.OrdinalIgnoreCase))
                _ownershipCache.TryRemove(key, out _);
    }

    /// <summary>指定したチームの現メンバー一覧を取得する。</summary>
    public async Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken = default)
    {
        var values = await http.GetPagedAsync($"teams/{teamId}/members?$top=999", cancellationToken);
        return ParseTeamMembers(values);
    }

    /// <summary>Graph応答のメンバー配列を<see cref="TeamMember"/>一覧へ変換する。</summary>
    private static List<TeamMember> ParseTeamMembers(IEnumerable<JsonElement> values)
    {
        return values.Select(x => new TeamMember(
                Required(x, "id"),
                Required(x, "userId"),
                Optional(x, "displayName") ?? "",
                Optional(x, "email") ?? "",
                x.TryGetProperty("roles", out var roles) &&
                roles.EnumerateArray().Any(r => r.GetString() == "owner")))
            .ToList();
    }

    /// <summary>
    /// 氏名またはメールアドレスからディレクトリ上のユーザーを検索する。
    /// まず直接参照を試み、見つからない場合は<see cref="SearchUsersAsync"/>にフォールバックする。
    /// </summary>
    public async Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = await http.GetAsync(
                $"users/{Uri.EscapeDataString(identifier)}?$select=id,displayName,userPrincipalName,mail",
                cancellationToken, expectedNotFound: true);
            return [ToDirectoryUser(doc.RootElement)];
        }
        catch (GraphException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await SearchUsersAsync(identifier, cancellationToken);
        }
    }

    // 直接参照(UPN/メールの完全一致)で見つからない場合のフォールバック。
    // 1) mail/UPNの完全一致フィルター、2) 表示名の全文検索、3) 表示名の前方一致、の順に緩めて試す。
    // 段階を分けることで、$search特有のあいまい一致による誤検出を最小限にしている。
    /// <summary>
    /// 直接参照で見つからなかった場合のフォールバック検索。メール/UPNの完全一致、
    /// 表示名の全文検索、表示名の前方一致の順に段階的に緩めて試す。
    /// </summary>
    private async Task<IReadOnlyList<DirectoryUser>> SearchUsersAsync(string identifier,
        CancellationToken cancellationToken)
    {
        var escaped = identifier.Replace("'", "''");
        var filter = Uri.EscapeDataString($"mail eq '{escaped}' or userPrincipalName eq '{escaped}'");
        var addressMatches = ToDirectoryUsers(await http.GetPagedAsync(
            $"users?$filter={filter}&$select=id,displayName,userPrincipalName,mail&$top=2",
            cancellationToken));
        if (addressMatches.Count > 0) return addressMatches;

        var searchText = identifier.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var search = Uri.EscapeDataString($"\"displayName:{searchText}\"");
        var nameMatches = ToDirectoryUsers(await http.GetPagedAsync(
            $"users?$search={search}&$count=true&$select=id,displayName,userPrincipalName,mail&$top=25",
            cancellationToken)).Where(user => UserIdentifier.NameEquals(user.DisplayName, identifier)).ToList();
        if (nameMatches.Count > 0) return nameMatches;

        var normalized = UserIdentifier.NormalizeName(identifier);
        if (normalized.Length == 0) return [];
        var firstCharacter = StringInfo.GetNextTextElement(normalized).Replace("'", "''");
        var nameFilter = Uri.EscapeDataString($"startswith(displayName,'{firstCharacter}')");
        return ToDirectoryUsers(await http.GetPagedAsync(
            $"users?$filter={nameFilter}&$select=id,displayName,userPrincipalName,mail&$top=100",
            cancellationToken)).Where(user => UserIdentifier.NameEquals(user.DisplayName, identifier)).ToList();
    }

    /// <summary>指定したユーザーをチームの一般メンバーとして追加する。</summary>
    public Task AddMemberAsync(string teamId, string userId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("チームへのメンバー追加を実行します。TeamId={TeamId}", teamId);
        return http.SendAsync(HttpMethod.Post, $"teams/{teamId}/members", new
        {
            odata_type = "#microsoft.graph.aadUserConversationMember",
            roles = Array.Empty<string>(),
            user_odata_bind = $"https://graph.microsoft.com/v1.0/users('{userId}')"
        }, true, cancellationToken);
    }

    /// <summary>指定したメンバーシップをチームから削除する。</summary>
    public Task RemoveMemberAsync(string teamId, string membershipId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("チームからのメンバー削除を実行します。TeamId={TeamId}", teamId);
        return http.SendAsync(HttpMethod.Delete,
            $"teams/{teamId}/members/{Uri.EscapeDataString(membershipId)}", cancellationToken: cancellationToken);
    }

    /// <summary>Graph応答のユーザー要素を<see cref="DirectoryUser"/>へ変換する。</summary>
    private static DirectoryUser ToDirectoryUser(JsonElement user)
    {
        return new DirectoryUser(Required(user, "id"), Required(user, "displayName"),
            Required(user, "userPrincipalName"), Optional(user, "mail"));
    }

    /// <summary>Graph応答のユーザー配列を<see cref="DirectoryUser"/>一覧へ変換する。</summary>
    private static List<DirectoryUser> ToDirectoryUsers(IEnumerable<JsonElement> users)
    {
        return users.Select(ToDirectoryUser).ToList();
    }

    /// <summary><see cref="GraphHttpClient.Required"/>への委譲。</summary>
    private static string Required(JsonElement element, string name) => GraphHttpClient.Required(element, name);

    /// <summary><see cref="GraphHttpClient.Optional"/>への委譲。</summary>
    private static string? Optional(JsonElement element, string name) => GraphHttpClient.Optional(element, name);
}
