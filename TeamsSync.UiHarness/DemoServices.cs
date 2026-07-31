using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.UiHarness;

internal sealed class DemoAuthenticationService : IAuthenticationService
{
    public string? UserName { get; private set; }
    public string? TenantId => UserName is null ? null : "demo-tenant";

    public Task<string> GetTokenAsync(bool interactive = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UserName = "デモ管理者";
        return Task.FromResult("demo-token-not-used");
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UserName = null;
        return Task.CompletedTask;
    }
}

internal sealed class DemoTeamsGateway : ITeamsGateway
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<TeamInfo> _teams =
    [
        new("demo-team", "UI確認用チーム", "追加・削除・保護・エラーを確認するためのデモチーム"),
        new("long-team", "非常に長いチーム名を使った高DPI・折り返しレイアウト確認用チーム", "長文確認用")
    ];
    private readonly Dictionary<string, List<TeamMember>> _members = new(StringComparer.OrdinalIgnoreCase)
    {
        ["demo-team"] =
        [
            new("membership-owner", "user-owner", "佐藤 オーナー", "owner@example.com", true),
            new("membership-taro", "user-taro", "山田 太郎", "taro@example.com", false),
            new("membership-hanako", "user-hanako", "鈴木 花子", "hanako@example.com", false),
            new("membership-long", "user-long", "とても長い表示名を持つレイアウト確認用ユーザー", "long.user@example.com", false)
        ],
        ["long-team"] =
        [
            new("long-owner-membership", "user-owner", "佐藤 オーナー", "owner@example.com", true),
            new("long-member-membership", "user-taro", "山田 太郎", "taro@example.com", false)
        ]
    };
    private readonly IReadOnlyList<DirectoryUser> _directory =
    [
        new("user-owner", "佐藤 オーナー", "owner@example.com", "owner@example.com"),
        new("user-taro", "山田 太郎", "taro@example.com", "taro@example.com"),
        new("user-hanako", "鈴木 花子", "hanako@example.com", "hanako@example.com"),
        new("user-long", "とても長い表示名を持つレイアウト確認用ユーザー", "long.user@example.com", "long.user@example.com"),
        new("user-new", "高橋 新規", "new.member@example.com", "new.member@example.com"),
        new("user-second", "田中 追加", "second.new@example.com", "second.new@example.com")
    ];

    public Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(("demo-admin", "デモ管理者", "demo.admin@example.com"));

    public Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
        CancellationToken cancellationToken = default) => Task.FromResult(_teams);

    public Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<TeamMember>>(
                _members.TryGetValue(teamId, out var members) ? members.ToList() : []);
    }

    public Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = identifier.Replace(" ", "", StringComparison.Ordinal)
            .Replace("　", "", StringComparison.Ordinal);
        IReadOnlyList<DirectoryUser> result = _directory.Where(user =>
            string.Equals(user.UserPrincipalName, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.Mail, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.DisplayName.Replace(" ", "", StringComparison.Ordinal)
                .Replace("　", "", StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(result);
    }

    public Task AddMemberAsync(string teamId, string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var user = _directory.Single(directoryUser =>
                string.Equals(directoryUser.Id, userId, StringComparison.OrdinalIgnoreCase));
            var members = _members[teamId];
            if (members.All(member => !string.Equals(member.UserId, userId, StringComparison.OrdinalIgnoreCase)))
                members.Add(new TeamMember($"demo-membership-{user.Id}", user.Id, user.DisplayName,
                    user.UserPrincipalName, false));
        }
        return Task.CompletedTask;
    }

    public Task RemoveMemberAsync(string teamId, string membershipId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            _members[teamId].RemoveAll(member =>
                string.Equals(member.MembershipId, membershipId, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
}

internal sealed class DemoUserPreferences : IUserPreferences
{
    public string? LastFolder { get; set; }
    public void Save() { }
}
