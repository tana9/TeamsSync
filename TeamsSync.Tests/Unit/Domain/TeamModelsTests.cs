using TeamsSync.Domain.Teams;

namespace TeamsSync.Tests.Unit.Domain;

public sealed class TeamModelsTests
{
    private static readonly TeamInfo Team = new("team-1", "開発", null);

    [Fact]
    public void SyncPlan_HasNoActionableChanges_追加も削除もエラーもない場合はtrue()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Keep, "維持太郎", "keep@example.com", ChangeReason.AlreadyMember)], []);

        Assert.True(plan.HasNoActionableChanges);
    }

    [Fact]
    public void SyncPlan_HasNoActionableChanges_追加がある場合はfalse()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam)], []);

        Assert.False(plan.HasNoActionableChanges);
    }

    [Fact]
    public void SyncPlan_HasNoActionableChanges_削除がある場合はfalse()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveNotInInput)], []);

        Assert.False(plan.HasNoActionableChanges);
    }

    [Fact]
    public void SyncPlan_HasNoActionableChanges_追加削除がなくてもエラーがある場合はfalse()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Error, "不明太郎", "unknown@example.com", ChangeReason.UserNotFound)], []);

        Assert.False(plan.HasNoActionableChanges);
    }

    [Fact]
    public void SyncPlan_Operations_追加と削除だけを抽出しそれ以外の種別は除外する()
    {
        SyncPlan plan = new(Team,
        [
            new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam),
            new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveNotInInput),
            new SyncChange(ChangeKind.Keep, "維持太郎", "keep@example.com", ChangeReason.AlreadyMember),
            new SyncChange(ChangeKind.Protected, "所有太郎", "owner@example.com", ChangeReason.OwnerProtected),
            new SyncChange(ChangeKind.NotMember, "未所属太郎", "notmember@example.com", ChangeReason.NotCurrentMember),
            new SyncChange(ChangeKind.Error, "不明太郎", "unknown@example.com", ChangeReason.UserNotFound)
        ], []);

        Assert.Equal([ChangeKind.Add, ChangeKind.Remove], plan.Operations.Select(x => x.Kind));
    }

    [Fact]
    public void SyncPlan_EnsureExecutable_未解決ユーザーがある場合は例外をスローする()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Error, "不明太郎", "unknown@example.com", ChangeReason.UserNotFound)], []);

        Assert.Throws<InvalidOperationException>(plan.EnsureExecutable);
    }

    [Fact]
    public void SyncPlan_EnsureExecutable_追加のみモードで削除が含まれる場合は例外をスローする()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveSpecified)], [],
            Mode: SyncMode.AddOnly);

        Assert.Throws<InvalidOperationException>(plan.EnsureExecutable);
    }

    [Fact]
    public void SyncPlan_EnsureExecutable_指定削除モードで追加が含まれる場合は例外をスローする()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam)], [],
            Mode: SyncMode.RemoveSpecified);

        Assert.Throws<InvalidOperationException>(plan.EnsureExecutable);
    }

    [Fact]
    public void SyncPlan_EnsureExecutable_実行可能な場合は例外をスローしない()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam)], []);

        Exception? exception = Record.Exception(plan.EnsureExecutable);

        Assert.Null(exception);
    }

    [Fact]
    public void SyncPlan_CanExecute_未解決ユーザーがある場合はfalse()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Error, "不明太郎", "unknown@example.com", ChangeReason.UserNotFound)], []);

        Assert.False(plan.CanExecute);
    }

    [Fact]
    public void SyncPlan_CanExecute_実行対象の変更が1件もない場合はfalse()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Keep, "維持太郎", "keep@example.com", ChangeReason.AlreadyMember)], []);

        Assert.False(plan.CanExecute);
    }

    [Fact]
    public void SyncPlan_CanExecute_追加のみモードで削除が含まれる場合はfalse()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveSpecified)], [],
            Mode: SyncMode.AddOnly);

        Assert.False(plan.CanExecute);
    }

    [Fact]
    public void SyncPlan_CanExecute_指定削除モードで追加が含まれる場合はfalse()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam)], [],
            Mode: SyncMode.RemoveSpecified);

        Assert.False(plan.CanExecute);
    }

    [Fact]
    public void SyncPlan_CanExecute_未解決がなくモードと矛盾せず実行対象がある場合はtrue()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam)], []);

        Assert.True(plan.CanExecute);
    }

    [Fact]
    public void SyncPlan_WithoutChanges_指定したユーザーIDの追加をExcludedへ変更する()
    {
        SyncPlan plan = new(Team,
        [
            new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam, "u1"),
            new SyncChange(ChangeKind.Add, "追加次郎", "add2@example.com", ChangeReason.AddToTeam, "u2")
        ], ["add@example.com", "add2@example.com"]);

        SyncPlan excluded = plan.WithoutChanges(["u1"]);

        SyncChange excludedChange = Assert.Single(excluded.Changes, x => x.UserId == "u1");
        Assert.Equal(ChangeKind.Excluded, excludedChange.Kind);
        Assert.Equal(ChangeReason.ManuallyExcluded, excludedChange.Reason);
        SyncChange remaining = Assert.Single(excluded.Changes, x => x.UserId == "u2");
        Assert.Equal(ChangeKind.Add, remaining.Kind);
        Assert.Equal(1, excluded.AddCount);
        Assert.Equal(1, excluded.ExcludedCount);
    }

    [Fact]
    public void SyncPlan_WithoutChanges_指定したユーザーIDの削除をExcludedへ変更する()
    {
        SyncPlan plan = new(Team,
        [
            new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveNotInInput, "u1",
                "m1")
        ], []);

        SyncPlan excluded = plan.WithoutChanges(["u1"]);

        SyncChange change = Assert.Single(excluded.Changes);
        Assert.Equal(ChangeKind.Excluded, change.Kind);
        Assert.Equal(0, excluded.RemoveCount);
        Assert.Equal(1, excluded.ExcludedCount);
    }

    [Fact]
    public void SyncPlan_WithoutChanges_一致しないユーザーIDは変更しない()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam, "u1")], []);

        SyncPlan result = plan.WithoutChanges(["other-user"]);

        Assert.Equal(ChangeKind.Add, Assert.Single(result.Changes).Kind);
        Assert.Equal(0, result.ExcludedCount);
    }

    [Fact]
    public void SyncPlan_WithoutChanges_ユーザーID比較は大文字小文字を区別しない()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam, "USER-1")], []);

        SyncPlan result = plan.WithoutChanges(["user-1"]);

        Assert.Equal(ChangeKind.Excluded, Assert.Single(result.Changes).Kind);
    }

    [Fact]
    public void SyncPlan_WithoutChanges_追加削除以外の種別はUserIdが一致しても変更しない()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Protected, "所有太郎", "owner@example.com", ChangeReason.OwnerProtected, "u1")],
            []);

        SyncPlan result = plan.WithoutChanges(["u1"]);

        Assert.Equal(ChangeKind.Protected, Assert.Single(result.Changes).Kind);
        Assert.Equal(0, result.ExcludedCount);
    }

    [Fact]
    public void SyncPlan_WithoutChanges_除外対象が空の場合は同一インスタンスを返す()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam, "u1")], []);

        SyncPlan result = plan.WithoutChanges([]);

        Assert.Same(plan, result);
    }

    [Fact]
    public void SyncPlan_WithoutChanges_全件除外すると実行対象がなくなり実行不可になる()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam, "u1")], []);

        SyncPlan excluded = plan.WithoutChanges(["u1"]);

        Assert.True(excluded.HasNoActionableChanges);
        Assert.False(excluded.CanExecute);
    }

    [Fact]
    public void SyncPlan_WithoutChanges_CurrentMemberCountとMembershipSnapshotは変化しない()
    {
        IReadOnlyList<TeamMembershipSnapshot> snapshot = [new("m1", "u1", false)];
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam, "u2")], [],
            5, snapshot);

        SyncPlan excluded = plan.WithoutChanges(["u2"]);

        Assert.Equal(5, excluded.CurrentMemberCount);
        Assert.Equal(snapshot, excluded.MembershipSnapshot);
    }

    [Fact]
    public void SyncPlan_IsEquivalentTo_モードが異なる場合はfalse()
    {
        SyncPlan preview = new(Team, [], [], Mode: SyncMode.AddOnly);
        SyncPlan latest = new(Team, [], [], Mode: SyncMode.FullSync);

        Assert.False(preview.IsEquivalentTo(latest));
    }

    [Fact]
    public void SyncPlan_IsEquivalentTo_メンバーシップスナップショットが異なる場合はfalse()
    {
        SyncPlan preview = new(Team, [], [],
            MembershipSnapshot: [new TeamMembershipSnapshot("m1", "u1", false)]);
        SyncPlan latest = new(Team, [], [],
            MembershipSnapshot: [new TeamMembershipSnapshot("m1", "u1", true)]);

        Assert.False(preview.IsEquivalentTo(latest));
    }

    [Fact]
    public void SyncPlan_IsEquivalentTo_変更なしの差分は比較対象に含めない()
    {
        SyncPlan preview = new(Team,
            [new SyncChange(ChangeKind.Keep, "A", "a@example.com", ChangeReason.AlreadyMember, "u1")], []);
        SyncPlan latest = new(Team,
        [
            new SyncChange(ChangeKind.Keep, "A", "a@example.com", ChangeReason.AlreadyMemberDifferentIdentifier,
                "u1")
        ], []);

        Assert.True(preview.IsEquivalentTo(latest));
    }

    [Fact]
    public void SyncPlan_IsEquivalentTo_同じ追加削除内容であればtrue()
    {
        SyncPlan preview = new(Team,
            [new SyncChange(ChangeKind.Add, "A", "a@example.com", ChangeReason.AddToTeam, "u1")], []);
        SyncPlan latest = new(Team,
            [new SyncChange(ChangeKind.Add, "A", "a@example.com", ChangeReason.AddToTeam, "u1")], []);

        Assert.True(preview.IsEquivalentTo(latest));
    }

    [Fact]
    public void SyncPlan_IsEquivalentTo_追加削除内容が異なる場合はfalse()
    {
        SyncPlan preview = new(Team,
            [new SyncChange(ChangeKind.Add, "A", "a@example.com", ChangeReason.AddToTeam, "u1")], []);
        SyncPlan latest = new(Team,
            [new SyncChange(ChangeKind.Add, "B", "b@example.com", ChangeReason.AddToTeam, "u2")], []);

        Assert.False(preview.IsEquivalentTo(latest));
    }
}