using System.Diagnostics;

namespace TeamsSync.Presentation.Services;

/// <summary>埋め込みマニュアルを一時フォルダーへ展開し、既定のブラウザーで開く</summary>
public sealed class WpfManualService : IManualService
{
    /// <inheritdoc />
    public void OpenManual()
    {
        string path = Path.Combine(Path.GetTempPath(), "TeamsSync", "Manual.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (Stream resource = typeof(WpfManualService).Assembly.GetManifestResourceStream("Manual.html")
                                 ?? throw new InvalidOperationException("埋め込みマニュアル Manual.html を読み込めません。"))
        using (FileStream file = File.Create(path))
        {
            resource.CopyTo(file);
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}