using TeamsSync.Domain.Teams;

namespace TeamsSync.Tests.Unit.Domain;

public sealed class SyncPlanFactoryTests
{
    private static readonly TeamInfo Team = new("team-1", "開発", null);

    [Fact]
    public void Create_所有者は入力になくても削除対象にならない()
    {
        TeamMember owner = Member("owner-membership", "owner-id", "Owner", "owner@example.com", true);
        TeamRoster roster = new([owner]);

        SyncPlan plan = SyncPlanFactory.Create(Team, roster, [], SyncMode.FullSync, []);

        Assert.DoesNotContain(plan.Changes, x => x.Kind == ChangeKind.Remove);
    }

    [Fact]
    public void Create_既存の所有者が入力にある場合はProtectedになる()
    {
        TeamMember owner = Member("owner-membership", "owner-id", "Owner", "owner@example.com", true);
        TeamRoster roster = new([owner]);
        AddressResolution[] resolutions =
            [new AddressResolution(ResolutionOutcome.ExistingSingle, "owner@example.com", owner)];

        SyncPlan plan = SyncPlanFactory.Create(Team, roster, resolutions, SyncMode.FullSync, ["owner@example.com"]);

        SyncChange change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Protected, change.Kind);
        Assert.Equal(ChangeReason.OwnerProtected, change.Reason);
    }

    [Fact]
    public void Create_追加のみモードでは入力にない既存メンバーを削除しない()
    {
        TeamMember existing = Member("m1", "u1", "Existing", "existing@example.com");
        TeamRoster roster = new([existing]);

        SyncPlan plan = SyncPlanFactory.Create(Team, roster, [], SyncMode.AddOnly, []);

        Assert.Empty(plan.Changes);
        Assert.Equal(0, plan.RemoveCount);
    }

    [Fact]
    public void Create_指定削除モードでは入力にある既存の一般メンバーを削除する()
    {
        TeamMember existing = Member("m1", "u1", "Existing", "existing@example.com");
        TeamRoster roster = new([existing]);
        AddressResolution[] resolutions =
            [new AddressResolution(ResolutionOutcome.ExistingSingle, "existing@example.com", existing)];

        SyncPlan plan = SyncPlanFactory.Create(Team, roster, resolutions, SyncMode.RemoveSpecified,
            ["existing@example.com"]);

        SyncChange change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Remove, change.Kind);
        Assert.Equal(ChangeReason.RemoveSpecified, change.Reason);
    }

    [Fact]
    public void Create_指定削除モードで現在メンバーでない新規ユーザーは未所属として扱う()
    {
        DirectoryUser user = new("u1", "New", "new@example.com", "new@example.com");
        AddressResolution[] resolutions = [new AddressResolution(ResolutionOutcome.NewUser, "new@example.com", User: user)];

        SyncPlan plan = SyncPlanFactory.Create(Team, new TeamRoster([]), resolutions, SyncMode.RemoveSpecified,
            ["new@example.com"]);

        SyncChange change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.NotMember, change.Kind);
    }

    [Fact]
    public void Create_完全同期モードでは入力にない一般メンバーを削除する()
    {
        TeamMember stale = Member("m1", "u1", "Stale", "stale@example.com");
        TeamRoster roster = new([stale]);

        SyncPlan plan = SyncPlanFactory.Create(Team, roster, [], SyncMode.FullSync, []);

        SyncChange change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Remove, change.Kind);
        Assert.Equal(ChangeReason.RemoveNotInInput, change.Reason);
    }

    [Fact]
    public void Create_曖昧なユーザーはエラーになる()
    {
        AddressResolution[] resolutions =
        [
            new AddressResolution(ResolutionOutcome.Error, "ambiguous@example.com",
                ErrorReason: ChangeReason.AmbiguousDirectoryUser)
        ];

        SyncPlan plan = SyncPlanFactory.Create(Team, new TeamRoster([]), resolutions, SyncMode.FullSync,
            ["ambiguous@example.com"]);

        SyncChange change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Error, change.Kind);
        Assert.Equal(ChangeReason.AmbiguousDirectoryUser, change.Reason);
    }

    [Fact]
    public void Create_同一ユーザーを複数アドレスで指定した場合は1行にまとめる()
    {
        TeamMember existing = Member("m1", "u1", "山田 太郎", "taro@example.com");
        TeamRoster roster = new([existing]);
        AddressResolution[] resolutions =
        [
            new AddressResolution(ResolutionOutcome.ExistingSingle, "taro@example.com", existing),
            new AddressResolution(ResolutionOutcome.ExistingSingle, "山田太郎", existing)
        ];

        SyncPlan plan = SyncPlanFactory.Create(Team, roster, resolutions, SyncMode.FullSync,
            ["taro@example.com", "山田太郎"]);

        SyncChange change = Assert.Single(plan.Changes);
        Assert.Equal("taro@example.com ／ 山田太郎", change.Email);
    }

    private static TeamMember Member(string membershipId, string userId, string name, string email,
        bool owner = false)
    {
        return new TeamMember(membershipId, userId, name, email, owner);
    }
}
