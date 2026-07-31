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
        var host = new RecordingHost { StopException = stopFails ? new InvalidOperationException("stop failed") : null };

        await AppHostShutdown.StopAndDisposeAsync(host, NullLogger.Instance, TimeSpan.FromSeconds(1));

        Assert.True(host.StopCalled);
        Assert.True(host.Disposed);
    }

    private sealed class RecordingHost : IHost
    {
        public IServiceProvider Services { get; } = new EmptyServiceProvider();
        public Exception? StopException { get; init; }
        public bool StopCalled { get; private set; }
        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            return StopException is null ? Task.CompletedTask : Task.FromException(StopException);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
