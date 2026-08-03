using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class SyncResultDisplayStateTests
{
    [Fact]
    public void Apply_実行結果から件数と失敗一覧と派生表示を構築する()
    {
        SyncResultDisplayState state = new();
        SyncExecutionResult execution = new(
        [
            new SyncOperationResult(ChangeKind.Add, "success@example.com", true, null),
            new SyncOperationResult(ChangeKind.Remove, "failure@example.com", false, "権限がありません")
        ], false);

        state.Apply(execution, @"C:\Logs\result.csv");
        state.RemainingCount = 1;

        Assert.True(state.HasResult);
        Assert.Equal(1, state.SuccessCount);
        Assert.Equal(1, state.FailureCount);
        Assert.Equal(2, state.ProcessedCount);
        Assert.True(state.HasFailedResults);
        Assert.Equal("failure@example.com", Assert.Single(state.FailedResults).Email);
        Assert.True(state.HasLog);
        Assert.True(state.NeedsRetry);
        Assert.Equal("未反映 1件", state.RemainingText);
        Assert.Contains("一部失敗", state.SummaryText);
    }

    [Fact]
    public void Clear_結果状態を未実行へ戻して必要な変更を通知する()
    {
        SyncResultDisplayState state = new();
        state.Apply(new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "failure@example.com", false, "失敗")], true),
            @"C:\Logs\result.csv");
        state.RemainingCount = 1;
        List<string?> raisedProperties = [];
        state.PropertyChanged += (_, args) => raisedProperties.Add(args.PropertyName);

        state.Clear();

        Assert.False(state.HasResult);
        Assert.False(state.HasLog);
        Assert.False(state.HasFailedResults);
        Assert.False(state.NeedsRetry);
        Assert.Null(state.RemainingCount);
        Assert.Contains(nameof(SyncResultDisplayState.HasFailedResults), raisedProperties);
        Assert.Contains(nameof(SyncResultDisplayState.NeedsRetry), raisedProperties);
        Assert.Contains(nameof(SyncResultDisplayState.HasRemainingCount), raisedProperties);
    }
}
