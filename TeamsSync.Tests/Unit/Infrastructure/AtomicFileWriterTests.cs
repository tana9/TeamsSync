using System.Text;

using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Tests.Unit.Infrastructure;

public sealed class AtomicFileWriterTests : IDisposable
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
    public void 書込失敗時は既存ファイルを維持して一時ファイルを残さない()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "before");

        Assert.Throws<IOException>(() => AtomicFileWriter.Write(path, stream =>
        {
            stream.Write(Encoding.UTF8.GetBytes("partial"));
            throw new IOException("write failed");
        }, true));

        Assert.Equal("before", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_directory));
    }
}