using Microsoft.Extensions.Logging;

using TeamsSync.Presentation.Services;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class WpfNotificationServiceTests
{
    [Fact]
    public void InvokeCallbackSafely_終了処理の例外を記録して呼び出し元へ伝播しない()
    {
        RecordingLogger logger = new();

        WpfNotificationService.InvokeCallbackSafely(
            () => throw new InvalidOperationException("callback failed"), "エラー", logger);

        Exception exception = Assert.Single(logger.Exceptions);
        Assert.Equal("callback failed", exception.Message);
    }

    [Fact]
    public async Task ShowErrorSafelyAsync_表示と終了処理の例外を記録して呼び出し元へ伝播しない()
    {
        RecordingLogger logger = new();
        bool callbackCalled = false;

        await WpfNotificationService.ShowErrorSafelyAsync(
            () => throw new InvalidOperationException("dialog failed"),
            "エラー", () =>
            {
                callbackCalled = true;
                throw new InvalidOperationException("callback failed");
            }, logger);

        Assert.True(callbackCalled);
        Assert.Equal(2, logger.Exceptions.Count);
        Assert.Collection(logger.Exceptions,
            exception => Assert.Equal("dialog failed", exception.Message),
            exception => Assert.Equal("callback failed", exception.Message));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<Exception> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                Exceptions.Add(exception);
            }
        }
    }
}
