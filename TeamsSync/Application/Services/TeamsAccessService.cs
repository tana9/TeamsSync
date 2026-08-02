using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>サインイン利用者、所有チーム、現在メンバーの参照ユースケースを提供する。</summary>
public sealed class TeamsAccessService(ITeamsGateway teamsGateway)
{
    public async Task<CurrentUser> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var user = await teamsGateway.GetMeAsync(cancellationToken);
        return new CurrentUser(user.Id, user.DisplayName, user.UserPrincipalName);
    }

    public Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId, bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (refresh)
        {
            teamsGateway.ClearOwnedTeamsCache(currentUserId);
        }

        return teamsGateway.GetOwnedTeamsAsync(currentUserId, cancellationToken);
    }

    public void ClearOwnedTeamsCache(string? currentUserId)
    {
        teamsGateway.ClearOwnedTeamsCache(currentUserId);
    }

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
