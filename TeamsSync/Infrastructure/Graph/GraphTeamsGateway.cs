using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
/// Microsoft Graph APIを介して、所有チームの判定、メンバー一覧の取得、
/// ユーザー検索、メンバーの追加・削除を行う。所有権キャッシュ(<see cref="TeamOwnershipCache"/>)、
/// バッチ取得の再試行(<see cref="TeamMembersBatchFetcher"/>)、ユーザー検索フォールバック
/// (<see cref="GraphUserSearchService"/>)を組み合わせるオーケストレーターとして振る舞う。
/// </summary>
public sealed class GraphTeamsGateway : ITeamsGateway
{
    private const int OwnedTeamLookupConcurrency = 3;
    private const int OwnedTeamBatchSize = 20;

    private readonly GraphHttpClient _http;
    private readonly GraphSdkClient _sdk;
    private readonly ILogger<GraphTeamsGateway> _logger;
    private readonly TeamOwnershipCache _ownershipCache = new();
    private readonly TeamMembersBatchFetcher _batchFetcher;
    private readonly GraphUserSearchService _userSearch;

    /// <summary>コンストラクター。</summary>
    public GraphTeamsGateway(GraphHttpClient http, GraphSdkClient sdk, ILogger<GraphTeamsGateway> logger)
    {
        _http = http;
        _sdk = sdk;
        _logger = logger;
        _batchFetcher = new TeamMembersBatchFetcher(http, logger);
        _userSearch = new GraphUserSearchService(sdk);
    }

    /// <summary>サインイン中のユーザー自身の情報を取得する。</summary>
    public async Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default)
    {
        var user = await _sdk.GetMeAsync(cancellationToken);
        return (Required(user.Id, "id"), Required(user.DisplayName, "displayName"),
            Required(user.UserPrincipalName, "userPrincipalName"));
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
        var teams = await _sdk.GetJoinedTeamsAsync(cancellationToken);
        var candidates = teams.Select(x => new TeamInfo(
            Required(x.Id, "id"), Required(x.DisplayName, "displayName"), x.Description)).ToList();

        var stopwatch = Stopwatch.StartNew();
        var cacheHits = 0;
        var ownership = new bool[candidates.Count];
        List<int> pendingIndexes = [];
        for (var i = 0; i < candidates.Count; i++)
        {
            if (_ownershipCache.TryGet(currentUserId, candidates[i].Id, out var cached))
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
        _logger.LogInformation(
            "所有チームを取得しました。Owned={OwnedCount}, Candidates={CandidateCount}, CacheHits={CacheHits}, ElapsedMs={ElapsedMs}, BatchCalls={BatchCalls}",
            owned.Count, candidates.Count, cacheHits, stopwatch.ElapsedMilliseconds, batchCalls);
        return owned;
    }

    /// <summary>
    /// 1バッチ分の候補チームについて、メンバー一覧をバッチ取得(失敗分は個別フォールバック)したうえで
    /// 所有者判定を行い、結果をキャッシュへ書き込む。
    /// </summary>
    private async Task ResolveOwnershipBatchAsync(string currentUserId, IReadOnlyList<TeamInfo> candidates,
        IReadOnlyList<int> batch, bool[] ownership, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var membersByIndex = await _batchFetcher.FetchAsync(candidates, batch, cancellationToken);

        foreach (var index in batch)
        {
            var team = candidates[index];
            var members = membersByIndex.TryGetValue(index, out var resolved)
                ? resolved
                : await FetchMembersIndividuallyAsync(team, cancellationToken);

            var isOwner = members.Any(member =>
                member.IsOwner && string.Equals(member.UserId, currentUserId,
                    StringComparison.OrdinalIgnoreCase));
            _ownershipCache.Set(currentUserId, team.Id, isOwner);
            ownership[index] = isOwner;
        }
    }

    /// <summary>バッチ取得できなかったチームのメンバー一覧を個別APIで再取得する。</summary>
    private async Task<List<TeamMember>> FetchMembersIndividuallyAsync(TeamInfo team,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "バッチ内のメンバー取得に失敗したため個別に再取得します。TeamId={TeamId}", team.Id);
        return (await GetTeamMembersAsync(team.Id, cancellationToken)).ToList();
    }

    /// <summary>所有チーム判定のキャッシュを消去する。ユーザーIDを指定するとそのユーザー分のみ消去する。</summary>
    public void ClearOwnedTeamsCache(string? currentUserId = null) => _ownershipCache.Clear(currentUserId);

    /// <summary>指定したチームの現メンバー一覧を取得する。</summary>
    public async Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken = default)
    {
        return GraphResponseParser.ParseTeamMembers(await _sdk.GetTeamMembersAsync(teamId, cancellationToken));
    }

    /// <summary>
    /// 氏名またはメールアドレスからディレクトリ上のユーザーを検索する。
    /// まず直接参照を試み、見つからない場合は<see cref="GraphUserSearchService"/>にフォールバックする。
    /// </summary>
    public async Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _sdk.GetUserAsync(identifier, cancellationToken);
            return user is null ? [] : [GraphResponseParser.ToDirectoryUser(user)];
        }
        catch (GraphException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await _userSearch.SearchAsync(identifier, cancellationToken);
        }
    }

    /// <summary>指定したユーザーをチームの一般メンバーとして追加する。</summary>
    public Task AddMemberAsync(string teamId, string userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("チームへのメンバー追加を実行します。TeamId={TeamId}", teamId);
        return _sdk.AddMemberAsync(teamId, userId, cancellationToken);
    }

    /// <summary>指定したメンバーシップをチームから削除する。</summary>
    public Task RemoveMemberAsync(string teamId, string membershipId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("チームからのメンバー削除を実行します。TeamId={TeamId}", teamId);
        return _sdk.RemoveMemberAsync(teamId, membershipId, cancellationToken);
    }

    private static string Required(string? value, string name) =>
        value ?? throw new InvalidDataException($"{name} がありません。");
}
