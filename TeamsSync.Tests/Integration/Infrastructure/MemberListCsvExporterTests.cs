using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Tests.Integration.Infrastructure;

public sealed class MemberListCsvExporterTests : IDisposable
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
    public void Export_表示名昇順で表示名メールアドレス役割の列を書き出す()
    {
        string path = Path.Combine(_directory, "members.csv");
        TeamMember[] members =
        [
            new TeamMember("membership-2", "user-2", "佐藤 花子", "sato@example.com", true),
            new TeamMember("membership-1", "user-1", "山田 太郎", "yamada@example.com", false)
        ];

        new MemberListCsvExporter().Export(members, path);

        string[] lines = File.ReadAllLines(path);
        Assert.Equal("表示名,メールアドレス,役割", lines[0]);
        Assert.Equal("\"佐藤 花子\",\"sato@example.com\",\"所有者\"", lines[1]);
        Assert.Equal("\"山田 太郎\",\"yamada@example.com\",\"メンバー\"", lines[2]);
    }

    [Fact]
    public void Export_指定フォルダーが存在しない場合は作成する()
    {
        Assert.False(Directory.Exists(_directory));
        string path = Path.Combine(_directory, "members.csv");

        new MemberListCsvExporter().Export(
            [new TeamMember("m1", "u1", "山田 太郎", "yamada@example.com", false)], path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Export_表示名内のダブルクォートをエスケープする()
    {
        string path = Path.Combine(_directory, "members.csv");

        new MemberListCsvExporter().Export(
            [new TeamMember("m1", "u1", "\"通称\" 太郎", "taro@example.com", false)], path);

        string content = File.ReadAllText(path);
        Assert.Contains("\"\"\"通称\"\" 太郎\"", content);
    }

    [Fact]
    public void Export_UTF8BOM付きで書き出す()
    {
        string path = Path.Combine(_directory, "members.csv");

        new MemberListCsvExporter().Export(
            [new TeamMember("m1", "u1", "山田 太郎", "yamada@example.com", false)], path);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void Export_既存ファイルを上書きする()
    {
        string path = Path.Combine(_directory, "members.csv");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "古い内容");

        new MemberListCsvExporter().Export(
            [new TeamMember("m1", "u1", "山田 太郎", "yamada@example.com", false)], path);

        string content = File.ReadAllText(path);
        Assert.DoesNotContain("古い内容", content);
        Assert.Contains("山田 太郎", content);
    }

    [Fact]
    public void SanitizeForFileName_禁止文字を置換し極端に長い値は切り詰める()
    {
        MemberListCsvExporter exporter = new();

        string sanitized = exporter.SanitizeForFileName(new string('あ', 300));

        Assert.Equal(100, sanitized.Length);
    }

    [Fact]
    public void SanitizeForFileName_禁止文字を置換する()
    {
        MemberListCsvExporter exporter = new();

        Assert.Equal("開発_総務", exporter.SanitizeForFileName("開発/総務"));
    }

    [Fact]
    public void SanitizeForFileName_空文字になる場合はフォールバックを返す()
    {
        MemberListCsvExporter exporter = new();

        Assert.Equal("チーム", exporter.SanitizeForFileName("   "));
    }
}
