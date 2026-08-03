using System.Text;

namespace TeamsSync;

internal static class StartupFailureLog
{
    internal static string? TryWrite(Exception exception, string? baseDirectory = null,
        TimeProvider? timeProvider = null)
    {
        try
        {
            string directory = baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TeamsSync", "Logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "startup-failure.log");
            File.AppendAllText(path,
                $"[{(timeProvider ?? TimeProvider.System).GetUtcNow():O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }
}