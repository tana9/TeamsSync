using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class SyncOperationDisplayStateTests
{
    [Fact]
    public void Preview_Startで進捗表示を初期化する()
    {
        SyncPreviewDisplayState state = new();

        IProgress<int> progress = state.Start(25);

        Assert.NotNull(progress);
        Assert.Equal(25, state.ProgressMaximum);
        Assert.Equal(0, state.ProgressValue);
        Assert.Equal("確認しています…", state.ProgressText);
    }

    [Fact]
    public void Execution_Reportで実行中の操作と件数を更新する()
    {
        SyncExecutionDisplayState state = new();
        state.Start(3);

        string text = state.Report(new SyncProgress(2, 3, ChangeKind.Remove, "user@example.com"));

        Assert.True(state.IsRunning);
        Assert.Equal(2, state.ProgressValue);
        Assert.Equal(3, state.ProgressMaximum);
        Assert.Equal("2 / 3件 — 削除中: user@example.com", text);

        state.Stop();
        Assert.False(state.IsRunning);
    }
}