using System.Diagnostics;

namespace TeamsSync.Presentation.Services;

/// <summary>保存済みファイルをWindowsの関連付けで開く。</summary>
public sealed class WpfSavedFileLauncher : ISavedFileLauncher
{
    /// <inheritdoc />
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("開くファイルが指定されていません。", nameof(path));
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
