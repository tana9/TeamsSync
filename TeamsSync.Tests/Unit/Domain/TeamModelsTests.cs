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
