using Microsoft.Extensions.Logging.Abstractions;
using TeamsSync.Infrastructure.Settings;

namespace TeamsSync.Tests.Integration.Infrastructure;

public sealed class JsonUserPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "TeamsSync.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_破損または空の設定を退避して初期値へ戻す(string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, content);

        var preferences = new JsonUserPreferences(path,
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
        var path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path,
            "{\"LastFolder\":\"C:\\\\members\"}");

        var preferences = new JsonUserPreferences(path,
            NullLogger<JsonUserPreferences>.Instance);

        Assert.Equal("C:\\members", preferences.LastFolder);
        Assert.Null(preferences.LoadWarning);
    }


    [Fact]
    public void Load_読み取りできない設定でも起動を継続する()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, "{\"LastFolder\":\"C:\\\\members\"}");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var preferences = new JsonUserPreferences(path,
            NullLogger<JsonUserPreferences>.Instance);

        Assert.Null(preferences.LastFolder);
        Assert.Contains("初期値", preferences.LoadWarning);
        Assert.True(File.Exists(path));
    }
}
