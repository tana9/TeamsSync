using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Abstractions;

public sealed class AuthenticationConfigurationException(string message) : InvalidOperationException(message);

public interface IAuthenticationService
{
    string? UserName { get; }
    string? TenantId { get; }
    Task<string> GetTokenAsync(bool interactive = false, CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
}

public interface ITeamsGateway
{
    Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier, CancellationToken cancellationToken = default);
    Task AddMemberAsync(string teamId, string userId, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(string teamId, string membershipId, CancellationToken cancellationToken = default);

    void ClearOwnedTeamsCache(string? currentUserId = null)
    {
    }
}

public interface IMemberListReader
{
    // ファイル解析は行数・列数によって時間がかかりうるため、UIスレッド外実行とキャンセルを呼び出し元が制御できるようにする
    MemberListDocument Read(string path, CancellationToken cancellationToken);
}

public interface IMemberTextParser
{
    MemberListDocument Parse(string text);
}

public interface ISyncResultWriter
{
    void WriteCsv(string path, SyncPlan plan, SyncExecutionResult result);
}

public interface IUserPreferences
{
    string? LoadWarning => null;
    string? LastFolder { get; set; }
    void Save();
}