namespace TeamsSync.Domain.Teams;

/// <summary>Teamsのチーム情報を表す。</summary>
/// <param name="Id">チームのオブジェクトID。</param>
/// <param name="DisplayName">表示名。</param>
/// <param name="Description">チームの説明(未設定の場合はnull)。</param>
public sealed record TeamInfo(string Id, string DisplayName, string? Description)
{
    /// <summary>UI表示用に<see cref="DisplayName" />を返す。</summary>
    public override string ToString()
    {
        return DisplayName;
    }
}
/// <summary>チームの現メンバー1名分の情報を表す。</summary>
/// <param name="MembershipId">メンバーシップのオブジェクトID(削除操作に使用)。</param>
/// <param name="UserId">ユーザーのオブジェクトID。</param>
/// <param name="DisplayName">表示名。</param>
/// <param name="Email">メールアドレス(UPN)。</param>
/// <param name="IsOwner">チームの所有者かどうか。</param>
public sealed record TeamMember(string MembershipId, string UserId, string DisplayName, string Email, bool IsOwner);

/// <summary>Microsoft Entra IDのディレクトリ上のユーザー情報を表す。</summary>
/// <param name="Id">ユーザーのオブジェクトID。</param>
/// <param name="DisplayName">表示名。</param>
/// <param name="UserPrincipalName">ユーザープリンシパル名。</param>
/// <param name="Mail">メールアドレス(未設定の場合はnull)。</param>
public sealed record DirectoryUser(string Id, string DisplayName, string UserPrincipalName, string? Mail);

/// <summary>同期差分における1件のメンバー操作の種別。</summary>
public enum ChangeKind
{
    /// <summary>メンバーとして追加する。</summary>
    Add,

    /// <summary>メンバーから削除する。</summary>
    Remove,

    /// <summary>変更せずそのまま維持する。</summary>
    Keep,

    /// <summary>所有者のため保護され、変更されない。</summary>
    Protected,

    /// <summary>指定削除の対象として入力されたが、現在はチームに所属していない。</summary>
    NotMember,

    /// <summary>アドレスの解決に失敗した(未解決)。</summary>
    Error
}

/// <summary>同期の実行モード。</summary>
public enum SyncMode
{
    /// <summary>追加のみを行い、リストにない既存メンバーは維持する。</summary>
    AddOnly,

    /// <summary>入力リストで指定した一般メンバーだけを削除する。</summary>
    RemoveSpecified,

    /// <summary>完全同期。リストにない一般メンバーを削除する。</summary>
    FullSync
}

/// <summary>同期差分における1件のメンバー変更内容を表す。</summary>
/// <param name="Kind">操作の種別。</param>
/// <param name="DisplayName">対象ユーザーの表示名。</param>
/// <param name="Email">入力アドレス(またはメールアドレス)。</param>
/// <param name="Detail">画面表示用の詳細説明。</param>
/// <param name="UserId">対象ユーザーのオブジェクトID(判明している場合)。</param>
/// <param name="MembershipId">対象メンバーシップのオブジェクトID(判明している場合)。</param>
public sealed record SyncChange(
    ChangeKind Kind,
    string DisplayName,
    string Email,
    string Detail,
    string? UserId = null,
    string? MembershipId = null);

/// <summary>
///     再検証(<see cref="TeamSyncService.RevalidatePlanAsync" />)用の、
///     プラン作成時点のメンバーシップのスナップショット。
/// </summary>
/// <param name="MembershipId">メンバーシップのオブジェクトID。</param>
/// <param name="UserId">ユーザーのオブジェクトID。</param>
/// <param name="IsOwner">所有者かどうか。</param>
public sealed record TeamMembershipSnapshot(string MembershipId, string UserId, bool IsOwner);

/// <summary>1回の同期で実行する変更内容をまとめた同期プラン。</summary>
/// <param name="Team">対象チーム。</param>
/// <param name="Changes">メンバーごとの変更内容一覧。</param>
/// <param name="InputAddresses">入力元のアドレス一覧。</param>
/// <param name="CurrentMemberCount">プラン作成時点の一般メンバー数。</param>
/// <param name="MembershipSnapshot">プラン作成時点のメンバーシップのスナップショット(再検証用)。</param>
/// <param name="Mode">同期の実行モード。</param>
public sealed record SyncPlan(
    TeamInfo Team,
    IReadOnlyList<SyncChange> Changes,
    IReadOnlyList<string> InputAddresses,
    int CurrentMemberCount = 0,
    IReadOnlyList<TeamMembershipSnapshot>? MembershipSnapshot = null,
    SyncMode Mode = SyncMode.FullSync)
{
    /// <summary>追加対象の件数。</summary>
    public int AddCount => Changes.Count(x => x.Kind == ChangeKind.Add);

    /// <summary>削除対象の件数。</summary>
    public int RemoveCount => Changes.Count(x => x.Kind == ChangeKind.Remove);

    /// <summary>変更なしの件数。</summary>
    public int KeepCount => Changes.Count(x => x.Kind == ChangeKind.Keep);

    /// <summary>所有者として保護された件数。</summary>
    public int ProtectedCount => Changes.Count(x => x.Kind == ChangeKind.Protected);

    /// <summary>指定削除の入力に含まれる、チーム未所属ユーザーの件数。</summary>
    public int NotMemberCount => Changes.Count(x => x.Kind == ChangeKind.NotMember);

    /// <summary>未解決(エラー)の件数。</summary>
    public int ErrorCount => Changes.Count(x => x.Kind == ChangeKind.Error);

    /// <summary>未解決の変更が1件以上あるかどうか。</summary>
    public bool HasErrors => ErrorCount > 0;
}
