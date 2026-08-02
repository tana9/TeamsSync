using TeamsSync.Presentation.ViewModels;
using TeamsSync.Tests.TestDoubles;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class BusyOperationRunnerTests
{
    [Fact]
    public async Task RunAsync_タイムアウトはキャンセルではなくエラーとして通知する()
    {
        RecordingNotificationService notifications = new();
        List<(string Message, bool IsError)> statuses = [];
        BusyOperationRunner runner = new(notifications,
            (message, isError) => statuses.Add((message, isError)));

        bool succeeded = await runner.RunAsync(
            () => Task.FromException(new TaskCanceledException("request timeout")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(succeeded);
        Assert.Equal("request timeout", notifications.ErrorMessage);
        Assert.Contains(statuses, status => status.IsError);
        Assert.DoesNotContain(statuses, status => status.Message == "処理を中止しました");
    }

    [Fact]
    public async Task RunAsync_呼び出し元のキャンセルはエラーを表示しない()
    {
        RecordingNotificationService notifications = new();
        List<(string Message, bool IsError)> statuses = [];
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        BusyOperationRunner runner = new(notifications,
            (message, isError) => statuses.Add((message, isError)));

        bool succeeded = await runner.RunAsync(
            () => Task.FromCanceled(cancellation.Token), cancellationToken: cancellation.Token);

        Assert.False(succeeded);
        Assert.Null(notifications.ErrorMessage);
        Assert.Contains(("処理を中止しました", false), statuses);
    }
}
