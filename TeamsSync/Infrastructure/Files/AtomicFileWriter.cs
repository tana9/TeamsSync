using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     一時ファイルへ書き込んでから対象パスへ置き換えることで、書き込み途中でのプロセス終了・クラッシュに
///     よって対象ファイルが壊れた状態のまま残らないようにする
/// </summary>
internal static class AtomicFileWriter
{
    /// <summary>一時ファイルへ内容を書き込み、完了後に対象パスへ原子的に置き換える</summary>
    /// <param name="path">最終的な保存先パス</param>
    /// <param name="write">一時ファイルのストリームへ内容を書き込む処理</param>
    /// <param name="overwrite">対象パスに既存ファイルがある場合に上書きするかどうか</param>
    /// <param name="identifierGenerator">一時ファイル名の生成に使うID生成器。省略時は既定の実装を使う</param>
    public static void Write(string path, Action<Stream> write, bool overwrite,
        IIdentifierGenerator? identifierGenerator = null)
    {
        string directory = Path.GetDirectoryName(path)
                           ?? throw new InvalidOperationException("保存先フォルダーを特定できません。");
        Directory.CreateDirectory(directory);
        IIdentifierGenerator ids = identifierGenerator ?? new IdentifierGenerator();
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{ids.NewGuid():N}.tmp");
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
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}