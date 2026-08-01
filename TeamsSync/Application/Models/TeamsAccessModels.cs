using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Models;

/// <summary>サインイン中のMicrosoft 365ユーザー。</summary>
public sealed record CurrentUser(string Id, string DisplayName, string UserPrincipalName);

/// <summary>Graph接続診断で確認した読取結果。</summary>
public sealed record TeamsConnectionDiagnostics(int OwnedTeamCount, int FirstTeamMemberCount,
    int CurrentUserSearchResultCount);

/// <summary>現在の一般メンバーをテキスト入力へ取り込むための変換結果。</summary>
public sealed record CurrentMemberImport(TeamInfo Team, IReadOnlyList<TeamMember> Members, string Text);
