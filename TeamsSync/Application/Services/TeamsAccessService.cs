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

    public async Task<TeamsConnectionDiagnostics> RunDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        CurrentUser user = await GetCurrentUserAsync(cancellationToken);
        IReadOnlyList<TeamInfo> teams = await GetOwnedTeamsAsync(user.Id, cancellationToken: cancellationToken);
        int memberCount = teams.Count == 0
            ? 0
            : (await teamsGateway.GetTeamMembersAsync(teams[0].Id, cancellationToken)).Count;
        IReadOnlyList<DirectoryUser> users = await teamsGateway.FindUsersAsync(
            user.UserPrincipalName, cancellationToken);
        return new TeamsConnectionDiagnostics(teams.Count, memberCount, users.Count);
    }

    public async Task<CurrentMemberImport?> ImportCurrentMembersAsync(TeamInfo team,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TeamMember> members = await teamsGateway.GetTeamMembersAsync(team.Id, cancellationToken);
        List<TeamMember> importable = members
            .Where(member => !member.IsOwner && !string.IsNullOrWhiteSpace(member.Email))
            .GroupBy(member => member.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(member => member.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (importable.Count == 0)
        {
            return null;
        }

        string text = string.Join(Environment.NewLine, importable.Select(FormatImportedMember));
        return new CurrentMemberImport(team, importable, text);
    }

    private static string FormatImportedMember(TeamMember member)
    {
        string displayName = new string(member.DisplayName
            .Select(character => char.IsControl(character) || character is '<' or '>' ? ' ' : character)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(displayName)
            ? member.Email.Trim()
            : $"{displayName} <{member.Email.Trim()}>";
    }
}
