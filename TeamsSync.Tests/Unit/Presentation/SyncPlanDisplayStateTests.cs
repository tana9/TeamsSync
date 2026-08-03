using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class SyncPlanDisplayStateTests
{
    [Fact]
    public void Apply_差分を表示順に並べフィルター件数と削除警告を更新する()
    {
        SyncPlanDisplayState state = new();
        SyncPlan plan = new(new TeamInfo("team-1", "開発", null),
        [
            new SyncChange(ChangeKind.Keep, "維持", "keep@example.com", ChangeReason.AlreadyMember),
            new SyncChange(ChangeKind.Add, "追加", "add@example.com", ChangeReason.AddToTeam),
            new SyncChange(ChangeKind.Error, "エラー", "error@example.com", ChangeReason.UserNotFound),
            new SyncChange(ChangeKind.Remove, "削除", "remove@example.com", ChangeReason.RemoveNotInInput)
        ], []);

        state.Apply(plan);

        Assert.True(state.HasPlan);
        Assert.Equal(
            [ChangeKind.Error, ChangeKind.Remove, ChangeKind.Add, ChangeKind.Keep],
            state.Changes.Select(change => change.Kind));
        Assert.Equal(1, state.Filters.Single(filter => filter.Kind == ChangeKind.Add).Count);
        Assert.True(state.IsRemovalWarningOpen);
        Assert.Contains("削除", state.RemovalWarningMessage);
    }

    [Fact]
    public void Clear_プランと一覧を消してフィルター件数を未確認へ戻す()
    {
        SyncPlanDisplayState state = new();
        state.Apply(new SyncPlan(new TeamInfo("team-1", "開発", null),
            [new SyncChange(ChangeKind.Add, "追加", "add@example.com", ChangeReason.AddToTeam)], []));

        state.Clear();

        Assert.Null(state.Current);
        Assert.False(state.HasPlan);
        Assert.Empty(state.Changes);
        Assert.All(state.Filters, filter => Assert.Null(filter.Count));
        Assert.False(state.IsRemovalWarningOpen);
        Assert.Equal("", state.SummaryText);
    }
}