namespace TeamsSync.Infrastructure.Files;

internal static class AtomicFileWriter
{
    public static void Write(string path, Action<Stream> write, bool overwrite)
    {
        string directory = Path.GetDirectoryName(path)
                           ?? throw new InvalidOperationException("保存先フォルダーを特定できません。");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }

            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
