using System.Text;

namespace TeamsSync;

internal static class StartupFailureLog
{
    internal static string? TryWrite(Exception exception, string? baseDirectory = null)
    {
        try
        {
            var directory = baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TeamsSync", "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup-failure.log");
            File.AppendAllText(path,
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
