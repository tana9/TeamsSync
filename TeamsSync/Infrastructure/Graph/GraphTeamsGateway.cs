using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;

using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

using TeamInfo = TeamsSync.Domain.Teams.TeamInfo;

namespace TeamsSync.Infrastructure.Graph;

// コンストラクター注入にすることで、batchFetcher・userSearchを単体テストからモックへ差し替えられるようにする
/// <summary>
///     Microsoft Graph APIを介して、所有チームの判定、メンバー一覧の取得、
///     ユーザー検索、メンバーの追加・削除を行う。所有権キャッシュ(<see cref="TeamOwnershipCache" />)、
///     バッチ取得の再試行(<see cref="TeamMembersBatchFetcher" />)、ユーザー検索フォールバック
///     (<see cref="GraphUserSearchService" />)を組み合わせるオーケストレーターとして振る舞う
/// </summary>
public sealed class GraphTeamsGateway(GraphSdkClient sdk, ILogger<GraphTeamsGateway> logger,
    TeamMembersBatchFetcher batchFetcher, GraphUserSearchService userSearch) : ITeamsGateway
{
    // GET /teams/{team-id}/members の公式スロットリング上限は「60 rps per app per tenant」
    // (リソース単位の追加上限は記載なし)。$batch内の各サブリクエストは個別に上限判定されるため
    // (https://learn.microsoft.com/en-us/graph/throttling)、並列3 × バッチ20件 = 最大60件の
    // メンバー取得サブリクエストが短時間に発行され得るこの構成は、既にテナント上限のすぐ下を
    // 突いている。安易に増やすと429が増える可能性が高いため、上げる場合は実テナントでの
    // 429発生率の計測が必須。
    // https://github.com/microsoftgraph/microsoft-graph-docs-contrib/blob/main/includes/throttling-teams.md
    private const int OwnedTeamLookupConcurrency = 3;
    private const int OwnedTeamBatchSize = 20;
    private readonly TeamOwnershipCache _ownershipCache = new();

    /// <summary>サインイン中のユーザー自身の情報を取得する</summary>
    public async Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default)
    {
        User user = await sdk.GetMeAsync(cancellationToken);
        return (GraphResponseParser.Required(user.Id, "id"),
            GraphResponseParser.Required(user.DisplayName, "displayName"),
            GraphResponseParser.Required(user.UserPrincipalName, "userPrincipalName"));
    }

    /// <summary>
    ///     サインイン中のユーザーが参加している全チームのうち、所有者になっているチームを判定して返す。
    ///     キャッシュ済みの判定結果を再利用しつつ、未判定のチームはバッチ取得で解決する
    /// </summary>
    public async Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
        CancellationToken cancellationToken = default)
    {
        // me/joinedTeamsは$topクエリオプションを許可しない(400 "Query option 'Top' is not allowed")ため、
        // 既定ページサイズのまま@odata.nextLinkでページングする
        IReadOnlyList<Team> teams = await sdk.GetJoinedTeamsAsync(cancellationToken);
        List<TeamInfo> candidates = teams.Select(x => new TeamInfo(
            GraphResponseParser.Required(x.Id, "id"), GraphResponseParser.Required(x.DisplayName, "displayName"),
            x.Description)).ToList();

        Stopwatch stopwatch = Stopwatch.StartNew();
        int cacheHits = 0;
        bool[] ownership = new bool[candidates.Count];
        List<int> pendingIndexes = [];
        for (int i = 0; i < candidates.Count; i++)
        {
            if (_ownershipCache.TryGet(currentUserId, candidates[i].Id, out bool cached))
            {
                cacheHits++;
                ownership[i] = cached;
            }
            else
            {
                pendingIndexes.Add(i);
            }
        }

        int batchCalls = 0;
        using SemaphoreSlim concurrency = new(OwnedTeamLookupConcurrency);
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

        List<TeamInfo> owned = candidates.Where((_, index) => ownership[index])
            .OrderBy(team => team.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        logger.LogInformation(
            "所有チームを取得しました。Owned={OwnedCount}, Candidates={CandidateCount}, CacheHits={CacheHits}, ElapsedMs={ElapsedMs}, BatchCalls={BatchCalls}",
            owned.Count, candidates.Count, cacheHits, stopwatch.ElapsedMilliseconds, batchCalls);
        return owned;
    }

    /// <summary>所有チーム判定のキャッシュを消去する。ユーザーIDを指定するとそのユーザー分のみ消去する</summary>
    public void ClearOwnedTeamsCache(string? currentUserId = null)
    {
        _ownershipCache.Clear(currentUserId);
    }

    /// <summary>指定したチームの現メンバー一覧を取得する</summary>
    public async Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken = default)
    {
        return GraphResponseParser.ParseTeamMembers(await sdk.GetTeamMembersAsync(teamId, cancellationToken));
    }

    /// <summary>
    ///     氏名またはメールアドレスからディレクトリ上のユーザーを検索する。
    ///     まず直接参照を試み、見つからない場合は<see cref="GraphUserSearchService" />にフォールバックする
    /// </summary>
    public async Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            User? user = await sdk.GetUserAsync(identifier, cancellationToken);
            return user is null ? [] : [GraphResponseParser.ToDirectoryUser(user)];
        }
        catch (GraphException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await userSearch.SearchAsync(identifier, cancellationToken);
        }
    }

    /// <summary>指定したユーザーをチームの一般メンバーとして追加する</summary>
    public Task AddMemberAsync(string teamId, string userId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("チームへのメンバー追加を実行します。TeamId={TeamId}", teamId);
        return sdk.AddMemberAsync(teamId, userId, cancellationToken);
    }

    /// <summary>指定したメンバーシップをチームから削除する</summary>
    public Task RemoveMemberAsync(string teamId, string membershipId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("チームからのメンバー削除を実行します。TeamId={TeamId}", teamId);
        return sdk.RemoveMemberAsync(teamId, membershipId, cancellationToken);
    }

    /// <summary>
    ///     1バッチ分の候補チームについて、メンバー一覧をバッチ取得(失敗分は個別フォールバック)したうえで
    ///     所有者判定を行い、結果をキャッシュへ書き込む
    /// </summary>
    private async Task ResolveOwnershipBatchAsync(string currentUserId, IReadOnlyList<TeamInfo> candidates,
        IReadOnlyList<int> batch, bool[] ownership, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<int, List<TeamMember>> membersByIndex =
            await batchFetcher.FetchAsync(candidates, batch, cancellationToken);

        foreach (int index in batch)
        {
            TeamInfo team = candidates[index];
            List<TeamMember>? members = membersByIndex.TryGetValue(index, out List<TeamMember>? resolved)
                ? resolved
                : await TryFetchMembersIndividuallyAsync(team, cancellationToken);

            // 個別取得も失敗した場合(members=null)は、そのチームだけ今回の所有者判定を諦めて
            // 一覧から除外する。動的Microsoft 365グループなどメンバーを直接取得できないチームが
            // 1件あっても、他の正常なチームの判定を継続できるようにする
            if (members is null)
            {
                // 取得不能と「所有者ではない」は別状態。一時的なGraph障害をfalseとして
                // キャッシュすると、手動更新するまで所有チームが一覧から消え続けてしまう。
                // 今回の一覧では除外するが、次回取得では再試行できるよう未キャッシュのままにする
                continue;
            }

            bool isOwner = new TeamRoster(members).FindByUserId(currentUserId)?.IsOwner ?? false;
            _ownershipCache.Set(currentUserId, team.Id, isOwner);
            ownership[index] = isOwner;
        }
    }

    /// <summary>
    ///     バッチ取得できなかったチームのメンバー一覧を個別APIで再取得する。個別取得も失敗した場合は
    ///     警告ログを残したうえでnullを返し、呼び出し元がそのチームだけ判定を諦められるようにする
    /// </summary>
    private async Task<List<TeamMember>?> TryFetchMembersIndividuallyAsync(TeamInfo team,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "バッチ内のメンバー取得に失敗したため個別に再取得します。TeamId={TeamId}", team.Id);
        try
        {
            return (await GetTeamMembersAsync(team.Id, cancellationToken)).ToList();
        }
        catch (GraphException ex)
        {
            logger.LogWarning(ex,
                "個別のメンバー取得にも失敗したため、このチームの所有者判定をスキップします。TeamId={TeamId}, StatusCode={StatusCode}",
                team.Id, ex.StatusCode);
            return null;
        }
        catch (InvalidDataException ex)
        {
            // メンバー情報が想定外の形状(userId欠落等)で解析できない場合も、GraphException時と同様に
            // このチームだけ所有者判定を諦め、他チームの判定を継続する
            logger.LogWarning(ex,
                "個別のメンバー取得結果を解析できなかったため、このチームの所有者判定をスキップします。TeamId={TeamId}",
                team.Id);
            return null;
        }
    }
}
