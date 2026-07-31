using System.Diagnostics;

namespace TeamsSync.Presentation.Services;

/// <summary>埋め込みマニュアルを一時フォルダーへ展開し、既定のブラウザーで開く。</summary>
public sealed class WpfManualService : IManualService
{
    public void OpenManual()
    {
        var path = Path.Combine(Path.GetTempPath(), "TeamsSync", "Manual.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var resource = typeof(WpfManualService).Assembly.GetManifestResourceStream("Manual.html")
                              ?? throw new InvalidOperationException("埋め込みマニュアル Manual.html を読み込めません。"))
        using (var file = File.Create(path))
            resource.CopyTo(file);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
