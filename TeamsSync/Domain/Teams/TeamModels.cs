namespace TeamsSync.Domain.Teams;

/// <summary>Teamsのチーム情報を表す。</summary>
/// <param name="Id">チームのオブジェクトID。</param>
/// <param name="DisplayName">表示名。</param>
/// <param name="Description">チームの説明(未設定の場合はnull)。</param>
public sealed record TeamInfo(string Id, string DisplayName, string? Description);

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
    Error,

    /// <summary>利用者が個別に除外したため、追加・削除の対象外になった。</summary>
    Excluded
}

/// <summary>同期差分がその状態になった業務上の理由。</summary>
public enum ChangeReason
{
    Unspecified,
    AmbiguousCurrentMember,
    AmbiguousDirectoryUser,
    UserNotFound,
    OwnerProtected,
    RemoveSpecified,
    AlreadyMember,
    AlreadyMemberDifferentIdentifier,
    NotCurrentMember,
    AddToTeam,
    RemoveNotInInput,
    ManuallyExcluded
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
/// <param name="Reason">変更種別を決定した業務上の理由。</param>
/// <param name="UserId">対象ユーザーのオブジェクトID(判明している場合)。</param>
/// <param name="MembershipId">対象メンバーシップのオブジェクトID(判明している場合)。</param>
public sealed record SyncChange(
    ChangeKind Kind,
    string DisplayName,
    string Email,
    ChangeReason Reason = ChangeReason.Unspecified,
    string? UserId = null,
    string? MembershipId = null);

/// <summary>
///     同期プランを再検証するための、プラン作成時点のメンバーシップのスナップショット。
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

    /// <summary>利用者が個別に除外した件数。</summary>
    public int ExcludedCount => Changes.Count(x => x.Kind == ChangeKind.Excluded);

    /// <summary>未解決の変更が1件以上あるかどうか。</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>未解決の変更がなく、追加・削除される一般メンバーも1人もいないかどうか。</summary>
    public bool HasNoActionableChanges => !HasErrors && AddCount == 0 && RemoveCount == 0;

    /// <summary>実際にGraphへ送信する操作(追加・削除)だけを抽出した一覧。</summary>
    public IReadOnlyList<SyncChange> Operations =>
        Changes.Where(x => x.Kind is ChangeKind.Add or ChangeKind.Remove).ToList();

    /// <summary>
    ///     選択した同期モードと、含まれる追加・削除の内容が矛盾していないかどうか。
    ///     「追加のみ」モードでの削除、「指定メンバーを削除」モードでの追加は矛盾とみなす。
    ///     <see cref="EnsureExecutable" />と<see cref="CanExecute" />が共通してこの判定を使う。
    /// </summary>
    private bool IsModeConsistent => Mode switch
    {
        SyncMode.AddOnly => RemoveCount == 0,
        SyncMode.RemoveSpecified => AddCount == 0,
        _ => true
    };

    /// <summary>未解決の変更がなく、モードと矛盾せず、かつ実行対象の変更が1件以上あるため実行可能かどうか。</summary>
    public bool CanExecute => !HasErrors && !HasNoActionableChanges && IsModeConsistent;

    /// <summary>
    ///     未解決の変更がある場合、または選択した同期モードと矛盾する追加・削除が含まれる場合に例外をスローする。
    /// </summary>
    public void EnsureExecutable()
    {
        if (HasErrors)
        {
            throw new InvalidOperationException("未解決ユーザーがあります。同期は実行できません。");
        }

        if (!IsModeConsistent)
        {
            throw new InvalidOperationException(Mode == SyncMode.AddOnly
                ? "追加のみモードではメンバーを削除できません。"
                : "指定メンバー削除モードではメンバーを追加できません。");
        }
    }

    /// <summary>
    ///     指定したユーザーID群を追加・削除の対象から個別に除外した新しい同期プランを返す。
    ///     除外された項目は<see cref="ChangeKind.Excluded" />として一覧には残るが、
    ///     <see cref="Operations" />・<see cref="AddCount" />・<see cref="RemoveCount" />には含まれず、
    ///     モード整合・所有者保護・件数整合(<see cref="CurrentMemberCount" />・
    ///     <see cref="MembershipSnapshot" />)は変化しない。
    /// </summary>
    public SyncPlan WithoutChanges(IReadOnlyCollection<string> userIds)
    {
        if (userIds.Count == 0)
        {
            return this;
        }

        HashSet<string> excluded = new(userIds, StringComparer.OrdinalIgnoreCase);
        List<SyncChange> updated = Changes.Select(change =>
            change.Kind is ChangeKind.Add or ChangeKind.Remove &&
            change.UserId is not null && excluded.Contains(change.UserId)
                ? change with { Kind = ChangeKind.Excluded, Reason = ChangeReason.ManuallyExcluded }
                : change).ToList();
        return this with { Changes = updated };
    }

    /// <summary>
    ///     このプランと<paramref name="other" />が、同期モード・メンバーシップのスナップショット・
    ///     追加/削除/保護/未所属/エラーの各操作内容の点で同一かどうかを判定する。
    /// </summary>
    public bool IsEquivalentTo(SyncPlan other)
    {
        if (Mode != other.Mode)
        {
            return false;
        }

        IReadOnlyList<TeamMembershipSnapshot> snapshot = MembershipSnapshot ?? [];
        IReadOnlyList<TeamMembershipSnapshot> otherSnapshot = other.MembershipSnapshot ?? [];
        if (!snapshot.SequenceEqual(otherSnapshot))
        {
            return false;
        }

        // Operationsプロパティ(追加・削除のみ)より広く、保護・未所属・エラーも比較対象に含めるため、
        // 同名の紛らわしさを避けてComparisonKeysという別名にしている。
        static IEnumerable<(ChangeKind Kind, string? UserId, string? MembershipId)> ComparisonKeys(SyncPlan plan)
        {
            return plan.Changes
                .Where(change => change.Kind is ChangeKind.Add or ChangeKind.Remove or
                    ChangeKind.Protected or ChangeKind.NotMember or ChangeKind.Error)
                .Select(change => (change.Kind, change.UserId, change.MembershipId))
                .OrderBy(change => change.Kind)
                .ThenBy(change => change.UserId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.MembershipId, StringComparer.OrdinalIgnoreCase);
        }

        return ComparisonKeys(this).SequenceEqual(ComparisonKeys(other));
    }
}