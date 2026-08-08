namespace TeamsSync.Domain.Teams;

/// <summary>1件のアドレス解決の結果種別</summary>
public enum ResolutionOutcome
{
    /// <summary>解決に失敗した</summary>
    Error,

    /// <summary>現メンバーの中に一意な一致があった</summary>
    ExistingSingle,

    /// <summary>ディレクトリ検索で一致した、現メンバーにいない新規ユーザー</summary>
    NewUser,

    /// <summary>ディレクトリ検索で一致したユーザーが、別のアドレスで既に現メンバーだった</summary>
    SameUserDifferentAddress
}

/// <summary>1件のアドレス解決結果</summary>
/// <param name="Outcome">解決結果の種別</param>
/// <param name="Address">入力アドレス</param>
/// <param name="ExistingMember">現メンバーとして一致した場合のメンバー情報</param>
/// <param name="User">ディレクトリ検索で一致したユーザー情報</param>
/// <param name="ErrorReason">解決に失敗した場合の理由</param>
/// <param name="MatchedByNameOnly">現メンバーとの一致がメールアドレスではなく氏名のみによる場合はtrue</param>
/// <param name="AmbiguousMembers">氏名等の一致で複数の現メンバーが該当し特定できなかった場合、その現メンバー一覧</param>
public sealed record AddressResolution(
    ResolutionOutcome Outcome,
    string Address,
    TeamMember? ExistingMember = null,
    DirectoryUser? User = null,
    ChangeReason ErrorReason = ChangeReason.Unspecified,
    bool MatchedByNameOnly = false,
    IReadOnlyList<TeamMember>? AmbiguousMembers = null);

/// <summary>
///     解決済みのアドレス一覧と現メンバー構成(<see cref="TeamRoster" />)から同期プランを組み立てる。
///     Graph通信を伴わない、純粋な業務ルールだけを担う
/// </summary>
public static class SyncPlanFactory
{
    // 同一ユーザーが複数表記で重複指定された際、2件目以降のEmailを最初の行へ結合する区切り文字。
    // 表示用の結合記号のため、UI側の解析やテストと同期させる際の参照点として定数化している
    private const string DuplicateAddressSeparator = " ／ ";

    /// <summary>
    ///     アドレスごとの解決結果を追加・維持・保護・エラーへ分類し、完全同期モードでは
    ///     リスト外の一般メンバーの削除も加えて、同期プランを作成する
    /// </summary>
    public static SyncPlan Create(TeamInfo team, TeamRoster roster,
        IReadOnlyList<AddressResolution> resolutions, SyncMode mode, IReadOnlyList<string> inputAddresses)
    {
        (Dictionary<string, DirectoryUser> resolved, List<SyncChange> changes, HashSet<string> ambiguousMemberIds) =
            ClassifyResolutions(resolutions, mode);
        if (mode == SyncMode.FullSync)
        {
            changes.AddRange(ComputeRemovals(roster.Members, resolved, changes, ambiguousMemberIds));
        }

        return new SyncPlan(team, changes, inputAddresses, roster.NonOwnerCount,
            roster.BuildMembershipSnapshot(), mode, roster.Members);
    }

    /// <summary>
    ///     アドレスごとの解決結果を、追加・維持・保護・エラーの各<see cref="SyncChange" />へ分類する。
    ///     同一ユーザーが氏名とメールアドレスなど複数の表記で重複指定された場合、2件目以降を別行にはせず
    ///     最初の行のEmailへ表記を合流させて1行にまとめる
    /// </summary>
    /// <returns>
    ///     解決済みユーザー、変更一覧に加え、氏名等の重複で特定できなかった現メンバーのユーザーID一覧
    ///     (完全同期の削除候補から除外するために<see cref="ComputeRemovals" />で使う)
    /// </returns>
    private static (Dictionary<string, DirectoryUser> Resolved, List<SyncChange> Changes,
        HashSet<string> AmbiguousMemberIds) ClassifyResolutions(
            IReadOnlyList<AddressResolution> resolutions, SyncMode mode)
    {
        // resolutions件数分ループする中で毎回SyncModePolicy.Forを呼び直さないよう、
        // モードに依存しないループの外で1回だけ解決する
        SyncModePolicy policy = SyncModePolicy.For(mode);
        Dictionary<string, DirectoryUser> resolved = new(StringComparer.OrdinalIgnoreCase);
        List<SyncChange> changes = [];
        Dictionary<string, int> rowIndexByUserId = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ambiguousMemberIds = new(StringComparer.OrdinalIgnoreCase);

        void AddOrMergeChange(string address, string userId, string? membershipId, string displayName,
            ChangeKind kind, ChangeReason reason)
        {
            if (rowIndexByUserId.TryGetValue(userId, out int index))
            {
                changes[index] = changes[index] with
                {
                    Email = $"{changes[index].Email}{DuplicateAddressSeparator}{address}"
                };
                return;
            }

            rowIndexByUserId[userId] = changes.Count;
            changes.Add(new SyncChange(kind, displayName, address, reason, userId, membershipId));
        }

        foreach (AddressResolution resolution in resolutions)
        {
            switch (resolution)
            {
                case
                {
                    Outcome: ResolutionOutcome.Error, ErrorReason: ChangeReason.AmbiguousCurrentMember,
                    AmbiguousMembers: { } ambiguous
                }:
                    changes.Add(new SyncChange(ChangeKind.Error, "", resolution.Address,
                        resolution.ErrorReason));
                    foreach (TeamMember member in ambiguous)
                    {
                        ambiguousMemberIds.Add(member.UserId);
                    }

                    break;
                case { Outcome: ResolutionOutcome.Error }:
                    changes.Add(new SyncChange(ChangeKind.Error, "", resolution.Address,
                        resolution.ErrorReason));
                    break;
                case { Outcome: ResolutionOutcome.ExistingSingle, ExistingMember: { } existing }:
                    (ChangeKind existingKind, ChangeReason existingReason) = ClassifyExistingMember(
                        existing.IsOwner, policy, ChangeReason.AlreadyMember, resolution.MatchedByNameOnly);
                    AddOrMergeChange(resolution.Address, existing.UserId, existing.MembershipId,
                        existing.DisplayName, existingKind, existingReason);
                    break;
                case
                {
                    Outcome: ResolutionOutcome.SameUserDifferentAddress, ExistingMember: { } sameUser,
                    User: { } sameUserAccount
                }:
                    resolved[resolution.Address] = sameUserAccount;
                    (ChangeKind sameUserKind, ChangeReason sameUserReason) = ClassifyExistingMember(
                        sameUser.IsOwner, policy, ChangeReason.AlreadyMemberDifferentIdentifier, false);
                    AddOrMergeChange(resolution.Address, sameUser.UserId, sameUser.MembershipId,
                        sameUser.DisplayName, sameUserKind, sameUserReason);
                    break;
                case { Outcome: ResolutionOutcome.NewUser, User: { } user }:
                    resolved[resolution.Address] = user;
                    (ChangeKind newUserKind, ChangeReason newUserReason) = policy.ClassifyNewUser();
                    AddOrMergeChange(resolution.Address, user.Id, null, user.DisplayName,
                        newUserKind, newUserReason);
                    break;
            }
        }

        return (resolved, changes, ambiguousMemberIds);
    }

    /// <summary>
    ///     既にチームに所属しているメンバーの変更種別・理由を判定する。所有者は常に保護し(モードに依存しない
    ///     データ側の不変条件のためここで判定する)、所有者でなければモード固有の扱いを
    ///     <see cref="SyncModePolicy.ClassifyMatchedNonOwner" />へ委ねる
    /// </summary>
    private static (ChangeKind Kind, ChangeReason Reason) ClassifyExistingMember(bool isOwner, SyncModePolicy policy,
        ChangeReason keepReason, bool matchedByNameOnly)
    {
        if (isOwner)
        {
            return (ChangeKind.Protected, ChangeReason.OwnerProtected);
        }

        return policy.ClassifyMatchedNonOwner(keepReason, matchedByNameOnly);
    }

    /// <summary>
    ///     完全同期モード用に、入力リストに含まれない一般メンバー(所有者を除く)を
    ///     削除対象の<see cref="SyncChange" />として列挙する。氏名等の重複で特定できなかった
    ///     (<paramref name="ambiguousMemberIds" />)現メンバーは、既にエラーとして表示されているため
    ///     削除候補としては二重に表示しない
    /// </summary>
    private static IEnumerable<SyncChange> ComputeRemovals(IReadOnlyList<TeamMember> current,
        Dictionary<string, DirectoryUser> resolved, IReadOnlyList<SyncChange> changes,
        IReadOnlySet<string> ambiguousMemberIds)
    {
        HashSet<string> wantedIds = resolved.Values.Select(x => x.Id)
            .Concat(changes.Where(x => x.Kind is ChangeKind.Keep or ChangeKind.Protected)
                .Select(x => x.UserId!))
            .Concat(ambiguousMemberIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return current.Where(x => !x.IsOwner && !wantedIds.Contains(x.UserId))
            .Select(member => new SyncChange(ChangeKind.Remove, member.DisplayName, member.Email,
                ChangeReason.RemoveNotInInput, member.UserId, member.MembershipId));
    }
}