namespace TeamsSync.Domain.Teams;

/// <summary>Teamsのチーム情報を表す</summary>
/// <param name="Id">チームのオブジェクトID</param>
/// <param name="DisplayName">表示名</param>
/// <param name="Description">チームの説明(未設定の場合はnull)</param>
public sealed record TeamInfo(string Id, string DisplayName, string? Description);

/// <summary>チームの現メンバー1名分の情報を表す</summary>
/// <param name="MembershipId">メンバーシップのオブジェクトID(削除操作に使用)</param>
/// <param name="UserId">ユーザーのオブジェクトID</param>
/// <param name="DisplayName">表示名</param>
/// <param name="Email">メールアドレス(UPN)</param>
/// <param name="IsOwner">チームの所有者かどうか</param>
public sealed record TeamMember(string MembershipId, string UserId, string DisplayName, string Email, bool IsOwner);

/// <summary>Microsoft Entra IDのディレクトリ上のユーザー情報を表す</summary>
/// <param name="Id">ユーザーのオブジェクトID</param>
/// <param name="DisplayName">表示名</param>
/// <param name="UserPrincipalName">ユーザープリンシパル名</param>
/// <param name="Mail">メールアドレス(未設定の場合はnull)</param>
public sealed record DirectoryUser(string Id, string DisplayName, string UserPrincipalName, string? Mail);

/// <summary>同期差分における1件のメンバー操作の種別</summary>
public enum ChangeKind
{
    /// <summary>メンバーとして追加する</summary>
    Add,

    /// <summary>メンバーから削除する</summary>
    Remove,

    /// <summary>変更せずそのまま維持する</summary>
    Keep,

    /// <summary>所有者のため保護され、変更されない</summary>
    Protected,

    /// <summary>指定削除の対象として入力されたが、現在はチームに所属していない</summary>
    NotMember,

    /// <summary>アドレスの解決に失敗した(未解決)</summary>
    Error,

    /// <summary>利用者が個別に除外したため、追加・削除の対象外になった</summary>
    Excluded
}

/// <summary>同期差分がその状態になった業務上の理由</summary>
public enum ChangeReason
{
    /// <summary>理由が指定されていない</summary>
    Unspecified,
    /// <summary>現在のチームに同じ識別子のメンバーが複数存在する</summary>
    AmbiguousCurrentMember,
    /// <summary>ディレクトリに一致するユーザーが複数存在する</summary>
    AmbiguousDirectoryUser,
    /// <summary>ディレクトリに一致するユーザーが存在しない</summary>
    UserNotFound,
    /// <summary>所有者であるため変更対象から保護された</summary>
    OwnerProtected,
    /// <summary>指定削除モードの入力に含まれている</summary>
    RemoveSpecified,
    /// <summary>既にチームのメンバーである</summary>
    AlreadyMember,
    /// <summary>別の識別子で解決された同一ユーザーが既にメンバーである</summary>
    AlreadyMemberDifferentIdentifier,
    /// <summary>現在はチームのメンバーではない</summary>
    NotCurrentMember,
    /// <summary>チームへの追加が必要である</summary>
    AddToTeam,
    /// <summary>完全同期の入力に含まれないため削除が必要である</summary>
    RemoveNotInInput,
    /// <summary>利用者が変更対象から個別に除外した</summary>
    ManuallyExcluded,
    /// <summary>現メンバーとの一致が氏名のみによるもので、メールアドレス等では確認できていない</summary>
    AlreadyMemberNameMatchOnly,
    /// <summary>指定削除の対象だが、現メンバーとの一致が氏名のみによるもので確度が低い</summary>
    RemoveSpecifiedNameMatchOnly
}

/// <summary>同期の実行モード</summary>
public enum SyncMode
{
    /// <summary>追加のみを行い、リストにない既存メンバーは維持する</summary>
    AddOnly,

    /// <summary>入力リストで指定した一般メンバーだけを削除する</summary>
    RemoveSpecified,

    /// <summary>完全同期。リストにない一般メンバーを削除する</summary>
    FullSync
}

/// <summary>同期差分における1件のメンバー変更内容を表す</summary>
/// <param name="Kind">操作の種別</param>
/// <param name="DisplayName">対象ユーザーの表示名</param>
/// <param name="Email">入力アドレス(またはメールアドレス)</param>
/// <param name="Reason">変更種別を決定した業務上の理由</param>
/// <param name="UserId">対象ユーザーのオブジェクトID(判明している場合)</param>
/// <param name="MembershipId">対象メンバーシップのオブジェクトID(判明している場合)</param>
public sealed record SyncChange(
    ChangeKind Kind,
    string DisplayName,
    string Email,
    ChangeReason Reason = ChangeReason.Unspecified,
    string? UserId = null,
    string? MembershipId = null);

/// <summary>
///     同期プランを再検証するための、プラン作成時点のメンバーシップのスナップショット
/// </summary>
/// <param name="MembershipId">メンバーシップのオブジェクトID</param>
/// <param name="UserId">ユーザーのオブジェクトID</param>
/// <param name="IsOwner">所有者かどうか</param>
public sealed record TeamMembershipSnapshot(string MembershipId, string UserId, bool IsOwner);