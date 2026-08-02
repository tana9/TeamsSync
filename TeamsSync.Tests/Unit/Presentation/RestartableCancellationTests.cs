using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class RestartableCancellationTests
{
    [Fact]
    public void Beginで未キャンセルの新しいトークンを取得しIsActiveになる()
    {
        RestartableCancellation cancellation = new();

        CancellationTokenSource cts = cancellation.Begin();

        Assert.False(cts.IsCancellationRequested);
        Assert.True(cancellation.IsActive);
    }

    [Fact]
    public void 再度Beginすると直前のトークンをキャンセルする()
    {
        RestartableCancellation cancellation = new();
        CancellationTokenSource first = cancellation.Begin();

        CancellationTokenSource second = cancellation.Begin();

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void Endで最新のトークンならIsActiveがfalseに戻り破棄される()
    {
        RestartableCancellation cancellation = new();
        CancellationTokenSource cts = cancellation.Begin();

        cancellation.End(cts);

        Assert.False(cancellation.IsActive);
        Assert.Throws<ObjectDisposedException>(() => cts.Token);
    }

    [Fact]
    public void 古いトークンでEndしても後発の実行には影響しない()
    {
        RestartableCancellation cancellation = new();
        CancellationTokenSource first = cancellation.Begin();
        CancellationTokenSource second = cancellation.Begin();

        cancellation.End(first);

        Assert.True(cancellation.IsActive);
        Assert.Throws<ObjectDisposedException>(() => first.Token);

        cancellation.End(second);

        Assert.False(cancellation.IsActive);
    }

    [Fact]
    public void Cancelは現在のトークンをキャンセルする()
    {
        RestartableCancellation cancellation = new();
        CancellationTokenSource cts = cancellation.Begin();

        cancellation.Cancel();

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void 実行中でない場合Cancelは何もしない()
    {
        RestartableCancellation cancellation = new();

        Exception? exception = Record.Exception(() => cancellation.Cancel());

        Assert.Null(exception);
    }
}