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
}
