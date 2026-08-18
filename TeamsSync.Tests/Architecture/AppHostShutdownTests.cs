using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace TeamsSync.Tests.Architecture;

public sealed class AppHostShutdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAndDisposeAsync_停止の成否にかかわらずHostを破棄する(bool stopFails)
    {
        RecordingHost host = new() { StopException = stopFails ? new InvalidOperationException("stop failed") : null };

        await AppHostShutdown.StopAndDisposeAsync(host, NullLogger.Instance, TimeSpan.FromSeconds(1));

        Assert.True(host.StopCalled);
        Assert.True(host.Disposed);
    }

    [Fact]
    public void StopAndDispose_非同期停止とHost破棄の完了まで戻らない()
    {
        RecordingHost host = new() { StopDelay = TimeSpan.FromMilliseconds(10) };

        AppHostShutdown.StopAndDispose(host, NullLogger.Instance, TimeSpan.FromSeconds(1));

        Assert.True(host.StopCalled);
        Assert.True(host.Disposed);
    }

    private sealed class RecordingHost : IHost
    {
        public Exception? StopException { get; init; }
        public TimeSpan StopDelay { get; init; }
        public bool StopCalled { get; private set; }
        public bool Disposed { get; private set; }
        public IServiceProvider Services { get; } = new EmptyServiceProvider();

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            if (StopDelay > TimeSpan.Zero)
            {
                await Task.Delay(StopDelay, cancellationToken);
            }

            if (StopException is not null)
            {
                throw StopException;
            }
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
