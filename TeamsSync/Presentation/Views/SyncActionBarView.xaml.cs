using System.Diagnostics;
using System.Windows.Navigation;

namespace TeamsSync.Presentation.Views;

/// <summary>差分確認・同期実行・進捗表示のアクションバーView。</summary>
public partial class SyncActionBarView
{
    /// <summary>コンストラクター。</summary>
    public SyncActionBarView()
    {
        InitializeComponent();
    }

    /// <summary>保存済みの同期結果CSVを、OSで関連付けられた既定のアプリで開く。</summary>
    private void OpenResultLog(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.LocalPath) { UseShellExecute = true });
        e.Handled = true;
    }
}
