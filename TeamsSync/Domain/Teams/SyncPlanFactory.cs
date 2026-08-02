namespace TeamsSync.Domain.Teams;

/// <summary>1件のアドレス解決の結果種別。</summary>
public enum ResolutionOutcome
{
    /// <summary>解決に失敗した。</summary>
    Error,

    /// <summary>現メンバーの中に一意な一致があった。</summary>
    ExistingSingle,

    /// <summary>ディレクトリ検索で一致した、現メンバーにいない新規ユーザー。</summary>
    NewUser,

    /// <summary>ディレクトリ検索で一致したユーザーが、別のアドレスで既に現メンバーだった。</summary>
    SameUserDifferentAddress
}

/// <summary>1件のアドレス解決結果。</summary>
/// <param name="Outcome">解決結果の種別。</param>
/// <param name="Address">入力アドレス。</param>
/// <param name="ExistingMember">現メンバーとして一致した場合のメンバー情報。</param>
/// <param name="User">ディレクトリ検索で一致したユーザー情報。</param>
/// <param name="ErrorReason">解決に失敗した場合の理由。</param>
public sealed record AddressResolution(
    ResolutionOutcome Outcome,
    string Address,
    TeamMember? ExistingMember = null,
    DirectoryUser? User = null,
    ChangeReason ErrorReason = ChangeReason.Unspecified);

/// <summary>
///     解決済みのアドレス一覧と現メンバー構成(<see cref="TeamRoster" />)から同期プランを組み立てる。
///     Graph通信を伴わない、純粋な業務ルールだけを担う。
/// </summary>
public static class SyncPlanFactory
{
    /// <summary>
    ///     アドレスごとの解決結果を追加・維持・保護・エラーへ分類し、完全同期モードでは
    ///     リスト外の一般メンバーの削除も加えて、同期プランを作成する。
    /// </summary>
    public static SyncPlan Create(TeamInfo team, TeamRoster roster,
        IReadOnlyList<AddressResolution> resolutions, SyncMode mode, IReadOnlyList<string> inputAddresses)
    {
        (Dictionary<string, DirectoryUser> resolved, List<SyncChange> changes) =
            ClassifyResolutions(resolutions, mode);
        if (mode == SyncMode.FullSync)
        {
            changes.AddRange(ComputeRemovals(roster.Members, resolved, changes));
        }

        return new SyncPlan(team, changes, inputAddresses, roster.NonOwnerCount,
            roster.BuildMembershipSnapshot(), mode);
    }

    /// <summary>
    ///     アドレスごとの解決結果を、追加・維持・保護・エラーの各<see cref="SyncChange" />へ分類する。
    ///     同一ユーザーが氏名とメールアドレスなど複数の表記で重複指定された場合、2件目以降を別行にはせず
    ///     最初の行のEmailへ表記を合流させて1行にまとめる。
    /// </summary>
    private static (Dictionary<string, DirectoryUser> Resolved, List<SyncChange> Changes) ClassifyResolutions(
        IReadOnlyList<AddressResolution> resolutions, SyncMode mode)
    {
        Dictionary<string, DirectoryUser> resolved = new(StringComparer.OrdinalIgnoreCase);
        List<SyncChange> changes = [];
        Dictionary<string, int> rowIndexByUserId = new(StringComparer.OrdinalIgnoreCase);

        void AddOrMergeChange(string address, string userId, string? membershipId, string displayName,
            ChangeKind kind, ChangeReason reason)
        {
            if (rowIndexByUserId.TryGetValue(userId, out int index))
            {
                changes[index] = changes[index] with { Email = $"{changes[index].Email} ／ {address}" };
                return;
            }

            rowIndexByUserId[userId] = changes.Count;
            changes.Add(new SyncChange(kind, displayName, address, reason, userId, membershipId));
        }

        foreach (AddressResolution resolution in resolutions)
        {
            switch (resolution)
            {
                case { Outcome: ResolutionOutcome.Error }:
                    changes.Add(new SyncChange(ChangeKind.Error, "", resolution.Address,
                        resolution.ErrorReason));
                    break;
                case { Outcome: ResolutionOutcome.ExistingSingle, ExistingMember: { } existing }:
                    (ChangeKind existingKind, ChangeReason existingReason) = ClassifyExistingMember(
                        existing.IsOwner, mode, ChangeReason.AlreadyMember);
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
                        sameUser.IsOwner, mode, ChangeReason.AlreadyMemberDifferentIdentifier);
                    AddOrMergeChange(resolution.Address, sameUser.UserId, sameUser.MembershipId,
                        sameUser.DisplayName, sameUserKind, sameUserReason);
                    break;
                case { Outcome: ResolutionOutcome.NewUser, User: { } user }:
                    resolved[resolution.Address] = user;
                    AddOrMergeChange(resolution.Address, user.Id, null, user.DisplayName,
                        mode == SyncMode.RemoveSpecified ? ChangeKind.NotMember : ChangeKind.Add,
                        mode == SyncMode.RemoveSpecified ? ChangeReason.NotCurrentMember : ChangeReason.AddToTeam);
                    break;
            }
        }

        return (resolved, changes);
    }

    /// <summary>
    ///     既にチームに所属しているメンバーの変更種別・理由を判定する。所有者は常に保護し、
    ///     所有者でなければ指定削除モードは削除、それ以外は<paramref name="keepReason" />を理由に維持する。
    /// </summary>
    private static (ChangeKind Kind, ChangeReason Reason) ClassifyExistingMember(bool isOwner, SyncMode mode,
        ChangeReason keepReason)
    {
        if (isOwner)
        {
            return (ChangeKind.Protected, ChangeReason.OwnerProtected);
        }

        return mode == SyncMode.RemoveSpecified
            ? (ChangeKind.Remove, ChangeReason.RemoveSpecified)
            : (ChangeKind.Keep, keepReason);
    }

    /// <summary>
    ///     完全同期モード用に、入力リストに含まれない一般メンバー(所有者を除く)を
    ///     削除対象の<see cref="SyncChange" />として列挙する。
    /// </summary>
    private static IEnumerable<SyncChange> ComputeRemovals(IReadOnlyList<TeamMember> current,
        Dictionary<string, DirectoryUser> resolved, IReadOnlyList<SyncChange> changes)
    {
        HashSet<string> wantedIds = resolved.Values.Select(x => x.Id)
            .Concat(changes.Where(x => x.Kind is ChangeKind.Keep or ChangeKind.Protected)
                .Select(x => x.UserId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return current.Where(x => !x.IsOwner && !wantedIds.Contains(x.UserId))
            .Select(member => new SyncChange(ChangeKind.Remove, member.DisplayName, member.Email,
                ChangeReason.RemoveNotInInput, member.UserId, member.MembershipId));
    }
}
