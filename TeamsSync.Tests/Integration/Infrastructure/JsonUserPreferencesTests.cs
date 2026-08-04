using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using TeamsSync.Infrastructure.Settings;

namespace TeamsSync.Tests.Integration.Infrastructure;

public sealed class JsonUserPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "TeamsSync.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_破損または空の設定を退避して初期値へ戻す(string content)
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, content);

        JsonUserPreferences preferences = new(path,
            NullLogger<JsonUserPreferences>.Instance);

        Assert.Null(preferences.LastFolder);
        Assert.Contains("初期値", preferences.LoadWarning);
        Assert.True(File.Exists(path));
        Assert.Single(Directory.GetFiles(_directory, "preferences.corrupt-*.json"));
    }

    [Fact]
    public void Load_正常な設定を読み込む()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path,
            "{\"LastFolder\":\"C:\\\\members\"}");

        JsonUserPreferences preferences = new(path,
            NullLogger<JsonUserPreferences>.Instance);

        Assert.Equal("C:\\members", preferences.LastFolder);
        Assert.Null(preferences.LoadWarning);
    }


    [Fact]
    public void Load_読み取りできない設定でも起動を継続する()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, "{\"LastFolder\":\"C:\\\\members\"}");
        using FileStream locked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        JsonUserPreferences preferences = new(path,
            NullLogger<JsonUserPreferences>.Instance);

        Assert.Null(preferences.LastFolder);
        Assert.Contains("初期値", preferences.LoadWarning);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Load_破損時のログにファイルの親ディレクトリ_個人情報を含むフルパス_を出力しない()
    {
        // 監査ログはアプリ全体のログプロバイダーへ流れるため、Windowsユーザー名を含むフルパスではなく
        // ファイル名だけを記録することを確認する回帰テスト
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, "{ invalid json");
        CapturingLogger<JsonUserPreferences> logger = new();

        _ = new JsonUserPreferences(path, logger);

        Assert.NotEmpty(logger.Records);
        Assert.All(logger.Records, record => Assert.DoesNotContain(_directory, record.Message));
        Assert.Contains(logger.Records, record => record.Message.Contains("preferences.json"));
    }

    private sealed record LogRecord(string Message);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogRecord> Records { get; } = [];

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
            Records.Add(new LogRecord(formatter(state, exception)));
        }
    }
}