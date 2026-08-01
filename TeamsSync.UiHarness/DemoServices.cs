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

public sealed class DemoTeamsGateway : ITeamsGateway
{
    private readonly IReadOnlyList<DirectoryUser> _directory =
    [
        new("user-owner", "佐藤 オーナー", "owner@example.com", "owner@example.com"),
        new("user-taro", "山田 太郎", "taro@example.com", "taro@example.com"),
        new("user-hanako", "鈴木 花子", "hanako@example.com", "hanako@example.com"),
        new("user-long", "とても長い表示名を持つレイアウト確認用ユーザー", "long.user@example.com", "long.user@example.com"),
        new("user-new", "高橋 新規", "new.member@example.com", "new.member@example.com"),
        new("user-second", "田中 追加", "second.new@example.com", "second.new@example.com"),
        new("user-same-1", "同姓 同名", "same.one@example.com", "same.one@example.com"),
        new("user-same-2", "同姓 同名", "same.two@example.com", "same.two@example.com")
    ];

    private readonly object _gate = new();

    private readonly Dictionary<string, List<TeamMember>> _members = new(StringComparer.OrdinalIgnoreCase)
    {
        ["demo-team"] =
        [
            new TeamMember("membership-owner", "user-owner", "佐藤 オーナー", "owner@example.com", true),
            new TeamMember("membership-taro", "user-taro", "山田 太郎", "taro@example.com", false),
            new TeamMember("membership-hanako", "user-hanako", "鈴木 花子", "hanako@example.com", false),
            new TeamMember("membership-long", "user-long", "とても長い表示名を持つレイアウト確認用ユーザー", "long.user@example.com", false)
        ],
        ["long-team"] =
        [
            new TeamMember("long-owner-membership", "user-owner", "佐藤 オーナー", "owner@example.com", true),
            new TeamMember("long-member-membership", "user-taro", "山田 太郎", "taro@example.com", false)
        ]
    };

    private readonly IReadOnlyList<TeamInfo> _teams =
    [
        new("demo-team", "UI確認用チーム", "追加・削除・保護・エラーを確認するためのデモチーム"),
        new("long-team", "非常に長いチーム名を使った高DPI・折り返しレイアウト確認用チーム", "長文確認用")
    ];

    public string? FailingUserId { get; set; }
    public TimeSpan OperationDelay { get; set; }
    public string? ThrottledIdentifier { get; set; }
    public string? FailingSearchIdentifier { get; set; }
    private bool _throttleHandled;

    public Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(("demo-admin", "デモ管理者", "demo.admin@example.com"));
    }

    public Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_teams);
    }

    public Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<TeamMember>>(
                _members.TryGetValue(teamId, out List<TeamMember>? members) ? members.ToList() : []);
        }
    }

    public async Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(FailingSearchIdentifier, identifier, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Harnessで再現したGraph検索エラーです。詳細コピーと閉じるボタンを確認してください。");
        }

        if (!_throttleHandled && string.Equals(ThrottledIdentifier, identifier, StringComparison.OrdinalIgnoreCase))
        {
            _throttleHandled = true;
            await Task.Delay(1200, cancellationToken);
        }

        string normalized = identifier.Replace(" ", "", StringComparison.Ordinal)
            .Replace("　", "", StringComparison.Ordinal);
        IReadOnlyList<DirectoryUser> result = _directory.Where(user =>
                string.Equals(user.UserPrincipalName, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Mail, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.DisplayName.Replace(" ", "", StringComparison.Ordinal)
                    .Replace("　", "", StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return result;
    }

    public async Task AddMemberAsync(string teamId, string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperationDelay > TimeSpan.Zero)
        {
            await Task.Delay(OperationDelay, cancellationToken);
        }
        if (string.Equals(FailingUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Harnessで再現したメンバー追加エラーです。");
        }

        lock (_gate)
        {
            DirectoryUser user = _directory.Single(directoryUser =>
                string.Equals(directoryUser.Id, userId, StringComparison.OrdinalIgnoreCase));
            List<TeamMember> members = _members[teamId];
            if (members.All(member => !string.Equals(member.UserId, userId, StringComparison.OrdinalIgnoreCase)))
            {
                members.Add(new TeamMember($"demo-membership-{user.Id}", user.Id, user.DisplayName,
                    user.UserPrincipalName, false));
            }
        }

    }

    public Task RemoveMemberAsync(string teamId, string membershipId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _members[teamId].RemoveAll(member =>
                string.Equals(member.MembershipId, membershipId, StringComparison.OrdinalIgnoreCase));
        }

        return Task.CompletedTask;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _members["demo-team"] =
            [
                new TeamMember("membership-owner", "user-owner", "佐藤 オーナー", "owner@example.com", true),
                new TeamMember("membership-taro", "user-taro", "山田 太郎", "taro@example.com", false),
                new TeamMember("membership-hanako", "user-hanako", "鈴木 花子", "hanako@example.com", false),
                new TeamMember("membership-long", "user-long", "とても長い表示名を持つレイアウト確認用ユーザー", "long.user@example.com",
                    false)
            ];
            _members["long-team"] =
            [
                new TeamMember("long-owner-membership", "user-owner", "佐藤 オーナー", "owner@example.com", true),
                new TeamMember("long-member-membership", "user-taro", "山田 太郎", "taro@example.com", false)
            ];
            FailingUserId = null;
            OperationDelay = TimeSpan.Zero;
            ThrottledIdentifier = null;
            FailingSearchIdentifier = null;
            _throttleHandled = false;
        }
    }
}

internal sealed class DemoUserPreferences : IUserPreferences
{
    public string? LastFolder { get; set; }
    public void Save() { }
}
