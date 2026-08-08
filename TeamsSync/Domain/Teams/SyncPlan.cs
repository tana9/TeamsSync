namespace TeamsSync.Domain.Teams;

/// <summary>1回の同期で実行する変更内容をまとめた同期プラン</summary>
/// <param name="Team">対象チーム</param>
/// <param name="Changes">メンバーごとの変更内容一覧</param>
/// <param name="InputAddresses">入力元のアドレス一覧</param>
/// <param name="CurrentMemberCount">プラン作成時点の一般メンバー数</param>
/// <param name="MembershipSnapshot">プラン作成時点のメンバーシップのスナップショット(再検証用)</param>
/// <param name="Mode">同期の実行モード</param>
/// <param name="CurrentMembers">プラン作成時点のチーム全メンバー(監査ログの「処理前メンバー一覧」に使用)</param>
public sealed record SyncPlan(
    TeamInfo Team,
    IReadOnlyList<SyncChange> Changes,
    IReadOnlyList<string> InputAddresses,
    int CurrentMemberCount = 0,
    IReadOnlyList<TeamMembershipSnapshot>? MembershipSnapshot = null,
    SyncMode Mode = SyncMode.FullSync,
    IReadOnlyList<TeamMember>? CurrentMembers = null)
{
    /// <summary>プラン作成時点のチーム全メンバー。未指定の場合は空一覧</summary>
    public IReadOnlyList<TeamMember> CurrentMembersOrEmpty => CurrentMembers ?? [];

    /// <summary>追加対象の件数</summary>
    public int AddCount => CountOf(ChangeKind.Add);

    /// <summary>削除対象の件数</summary>
    public int RemoveCount => CountOf(ChangeKind.Remove);

    /// <summary>変更なしの件数</summary>
    public int KeepCount => CountOf(ChangeKind.Keep);

    /// <summary>所有者として保護された件数</summary>
    public int ProtectedCount => CountOf(ChangeKind.Protected);

    /// <summary>指定削除の入力に含まれる、チーム未所属ユーザーの件数</summary>
    public int NotMemberCount => CountOf(ChangeKind.NotMember);

    /// <summary>未解決(エラー)の件数</summary>
    public int ErrorCount => CountOf(ChangeKind.Error);

    /// <summary>利用者が個別に除外した件数</summary>
    public int ExcludedCount => CountOf(ChangeKind.Excluded);

    private int CountOf(ChangeKind kind) => Changes.Count(x => x.Kind == kind);

    /// <summary>未解決の変更が1件以上あるかどうか</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>未解決の変更がなく、追加・削除される一般メンバーも1人もいないかどうか</summary>
    public bool HasNoActionableChanges => !HasErrors && AddCount == 0 && RemoveCount == 0;

    /// <summary>実際にGraphへ送信する操作(追加・削除)だけを抽出した一覧</summary>
    public IReadOnlyList<SyncChange> Operations => Changes.Where(x => x.IsOperation).ToList();

    /// <summary>
    ///     選択した同期モードと、含まれる追加・削除の内容が矛盾していないかどうか。
    ///     「追加のみ」モードでの削除、「指定メンバーを削除」モードでの追加は矛盾とみなす。
    ///     <see cref="EnsureExecutable" />と<see cref="CanExecute" />が共通してこの判定を使う
    /// </summary>
    private bool IsModeConsistent
    {
        get
        {
            SyncModePolicy policy = SyncModePolicy.For(Mode);
            return (policy.AllowsRemove || RemoveCount == 0) && (policy.AllowsAdd || AddCount == 0);
        }
    }

    /// <summary>未解決の変更がなく、モードと矛盾せず、かつ実行対象の変更が1件以上あるため実行可能かどうか</summary>
    public bool CanExecute => !HasErrors && !HasNoActionableChanges && IsModeConsistent;

    /// <summary>
    ///     未解決の変更がある場合、選択した同期モードと矛盾する追加・削除が含まれる場合、
    ///     または実行対象の変更が1件もない場合に例外をスローする。<see cref="CanExecute" />と
    ///     同じ条件を判定する
    /// </summary>
    public void EnsureExecutable()
    {
        if (HasErrors)
        {
            throw new InvalidOperationException("未解決ユーザーがあります。同期は実行できません。");
        }

        if (!IsModeConsistent)
        {
            throw new InvalidOperationException(SyncModePolicy.For(Mode).InconsistentOperationMessage);
        }

        if (HasNoActionableChanges)
        {
            throw new InvalidOperationException("反映する変更がありません。同期は実行できません。");
        }
    }

    /// <summary>
    ///     指定したユーザーID群を追加・削除の対象から個別に除外した新しい同期プランを返す。
    ///     除外された項目は<see cref="ChangeKind.Excluded" />として一覧には残るが、
    ///     <see cref="Operations" />・<see cref="AddCount" />・<see cref="RemoveCount" />には含まれず、
    ///     モード整合・所有者保護・件数整合(<see cref="CurrentMemberCount" />・
    ///     <see cref="MembershipSnapshot" />)は変化しない
    /// </summary>
    public SyncPlan WithoutChanges(IReadOnlyCollection<string> userIds)
    {
        if (userIds.Count == 0)
        {
            return this;
        }

        HashSet<string> excluded = new(userIds, StringComparer.OrdinalIgnoreCase);
        List<SyncChange> updated = Changes.Select(change =>
            change.IsOperation &&
            change.UserId is not null && excluded.Contains(change.UserId)
                ? change with { Kind = ChangeKind.Excluded, Reason = ChangeReason.ManuallyExcluded }
                : change).ToList();
        return this with { Changes = updated };
    }

    /// <summary>
    ///     このプランと<paramref name="other" />が、同期モード・メンバーシップのスナップショット・
    ///     追加/削除/保護/未所属/エラーの各操作内容の点で同一かどうかを判定する
    /// </summary>
    public bool IsEquivalentTo(SyncPlan other)
    {
        return SyncPlanEquivalence.AreEquivalent(this, other);
    }
}

// SyncPlanレコード自体にCanExecute/EnsureExecutable/WithoutChangesと業務ロジックが集中していたため、
// 単体では40行近くあった同値比較ロジックだけを切り出し、レコードの見通しを良くしている
/// <summary>同期プラン同士の同値性(再検証で「そのまま実行してよいか」の判定基準)を判定する</summary>
internal static class SyncPlanEquivalence
{
    /// <summary>
    ///     2つの同期プランが、同期モード・メンバーシップのスナップショット・
    ///     追加/削除/保護/未所属/エラーの各操作内容の点で同一かどうかを判定する
    /// </summary>
    public static bool AreEquivalent(SyncPlan first, SyncPlan second)
    {
        if (first.Mode != second.Mode)
        {
            return false;
        }

        IReadOnlyList<TeamMembershipSnapshot> snapshot = first.MembershipSnapshot ?? [];
        IReadOnlyList<TeamMembershipSnapshot> otherSnapshot = second.MembershipSnapshot ?? [];
        if (!snapshot.SequenceEqual(otherSnapshot))
        {
            return false;
        }

        // WithoutChangesで個別除外(Excluded)したユーザーは、除外前の種別(追加/削除)が失われて
        // いるため比較から取り除く。除外の有無自体はメンバー構成の変化ではなく、除外後に
        // secondを再構築してもExcludedという分類は再現されない(SyncPlanFactoryはExcludedを
        // 生成しない)ため、素直に比較するとexcluded分の要素数が食い違い常に不一致になってしまう
        HashSet<string> excludedUserIds = first.Changes
            .Where(change => change.Kind == ChangeKind.Excluded && change.UserId is not null)
            .Select(change => change.UserId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ComparisonKeys(first, excludedUserIds).SequenceEqual(ComparisonKeys(second, excludedUserIds));
    }

    // Operationsプロパティ(追加・削除のみ)より広く、保護・未所属・エラーも比較対象に含めるため、
    // 同名の紛らわしさを避けてComparisonKeysという別名にしている
    private static IEnumerable<(ChangeKind Kind, string? UserId, string? MembershipId)> ComparisonKeys(
        SyncPlan plan, IReadOnlySet<string> excludedUserIds)
    {
        return plan.Changes
            .Where(change => change.Kind is ChangeKind.Add or ChangeKind.Remove or
                ChangeKind.Protected or ChangeKind.NotMember or ChangeKind.Error)
            .Where(change => change.UserId is null || !excludedUserIds.Contains(change.UserId))
            .Select(change => (change.Kind, change.UserId, change.MembershipId))
            .OrderBy(change => change.Kind)
            .ThenBy(change => change.UserId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(change => change.MembershipId, StringComparer.OrdinalIgnoreCase);
    }
}