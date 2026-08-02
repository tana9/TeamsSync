using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>サインイン利用者、所有チーム、現在メンバーの参照ユースケースを提供する</summary>
public sealed class TeamsAccessService(ITeamsGateway teamsGateway)
{
    /// <summary>サインイン中のユーザー情報を取得する</summary>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>サインイン中のユーザー情報</returns>
    public async Task<CurrentUser> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        (string Id, string DisplayName, string UserPrincipalName) user =
            await teamsGateway.GetMeAsync(cancellationToken);
        return new CurrentUser(user.Id, user.DisplayName, user.UserPrincipalName);
    }

    /// <summary>指定したユーザーが所有するチームを取得する</summary>
    /// <param name="currentUserId">所有チームを取得するユーザーのオブジェクトID</param>
    /// <param name="refresh">キャッシュを破棄して再取得する場合はtrue</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>ユーザーが所有するチームの一覧</returns>
    public Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId, bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (refresh)
        {
            teamsGateway.ClearOwnedTeamsCache(currentUserId);
        }

        return teamsGateway.GetOwnedTeamsAsync(currentUserId, cancellationToken);
    }

    /// <summary>所有チーム判定のキャッシュを消去する</summary>
    /// <param name="currentUserId">対象ユーザーのオブジェクトID。全件を消去する場合はnull</param>
    public void ClearOwnedTeamsCache(string? currentUserId)
    {
        teamsGateway.ClearOwnedTeamsCache(currentUserId);
    }

    /// <summary>指定したチームの現在の一般メンバーを入力用テキストとして取り込む</summary>
    /// <param name="team">メンバーを取り込むチーム</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>取り込み可能な一般メンバーがいる場合は取り込み結果。それ以外の場合はnull</returns>
    public async Task<CurrentMemberImport?> ImportCurrentMembersAsync(TeamInfo team,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TeamMember> members = await teamsGateway.GetTeamMembersAsync(team.Id, cancellationToken);
        IReadOnlyList<TeamMember> importable = new TeamRoster(members).ImportableMembers();
        if (importable.Count == 0)
        {
            return null;
        }

        string text = string.Join(Environment.NewLine,
            importable.Select(member => MemberIdentifierLine.FormatDisplayLine(member.DisplayName, member.Email)));
        return new CurrentMemberImport(team, importable, text);
    }
}
