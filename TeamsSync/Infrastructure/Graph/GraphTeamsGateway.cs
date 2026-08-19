using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;

using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

using TeamInfo = TeamsSync.Domain.Teams.TeamInfo;

namespace TeamsSync.Infrastructure.Graph;

// コンストラクター注入にすることで、userSearchを単体テストからモックへ差し替えられるようにする
/// <summary>
///     Microsoft Graph APIを介して、所有チームの判定、メンバー一覧の取得、
///     ユーザー検索、メンバーの追加・削除を行う。ユーザー検索フォールバック(<see cref="GraphUserSearchService" />)
///     を組み合わせるオーケストレーターとして振る舞う
/// </summary>
public sealed class GraphTeamsGateway(GraphSdkClient sdk, ILogger<GraphTeamsGateway> logger,
    GraphUserSearchService userSearch) : ITeamsGateway
{
    /// <summary>サインイン中のユーザー自身の情報を取得する</summary>
    public async Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default)
    {
        User user = await sdk.GetMeAsync(cancellationToken);
        return (GraphResponseParser.Required(user.Id, "id"),
            GraphResponseParser.Required(user.DisplayName, "displayName"),
            GraphResponseParser.Required(user.UserPrincipalName, "userPrincipalName"));
    }

    // 以前はチームごとにメンバー一覧を取得して自分が含まれるか判定していたが(Graph APIに
    // 「所有チーム一覧」を直接返すエンドポイントがないため)、参加チーム数に比例して呼び出し回数が
    // 増えスロットリングの懸念もあった。/me/ownedObjectsは所有するグループ(Teamsの実体)IDを
    // 1回の呼び出しで返すため、参加チーム一覧との積集合を取るだけで済み、チーム数に関わらず
    // 常にGraph呼び出し2回(joinedTeams・ownedObjects)で完結する
    /// <summary>サインイン中のユーザーが参加している全チームのうち、所有者になっているチームを判定して返す</summary>
    public async Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        // me/joinedTeamsは$topクエリオプションを許可しない(400 "Query option 'Top' is not allowed")ため、
        // 既定ページサイズのまま@odata.nextLinkでページングする
        Task<IReadOnlyList<Team>> joinedTeamsTask = sdk.GetJoinedTeamsAsync(cancellationToken);
        Task<IReadOnlyList<DirectoryObject>> ownedObjectsTask = sdk.GetOwnedObjectsAsync(cancellationToken);
        await Task.WhenAll(joinedTeamsTask, ownedObjectsTask);

        HashSet<string> ownedTeamIds = GraphResponseParser.ParseOwnedTeamIds(ownedObjectsTask.Result);
        List<TeamInfo> owned = joinedTeamsTask.Result
            .Where(team => team.Id is not null && ownedTeamIds.Contains(team.Id))
            .Select(team => new TeamInfo(GraphResponseParser.Required(team.Id, "id"),
                GraphResponseParser.Required(team.DisplayName, "displayName"), team.Description))
            .OrderBy(team => team.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        logger.LogInformation(
            "所有チームを取得しました。Owned={OwnedCount}, Joined={JoinedCount}, ElapsedMs={ElapsedMs}",
            owned.Count, joinedTeamsTask.Result.Count, stopwatch.ElapsedMilliseconds);
        return owned;
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
}
