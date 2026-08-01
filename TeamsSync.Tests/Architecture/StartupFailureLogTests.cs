namespace TeamsSync.Tests.Architecture;

public sealed class StartupFailureLogTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TeamsSync.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void TryWrite_起動例外の詳細を診断ログへ記録する()
    {
        string? path = StartupFailureLog.TryWrite(new InvalidOperationException("startup failed"), _directory);

        Assert.NotNull(path);
        Assert.Contains("startup failed", File.ReadAllText(path));
    }
}