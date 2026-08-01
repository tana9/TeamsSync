using System.ComponentModel;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TeamsSync.Presentation.Views;

/// <summary>アプリケーションのメインウィンドウ。</summary>
public partial class MainWindow
{
    private readonly MainWindowViewModel _viewModel;
    private bool _closeAfterCancellation;

    /// <summary>コンストラクター。ダイアログ・スナックバーホストを登録し、テーマを適用する。</summary>
    public MainWindow(MainWindowViewModel viewModel, IUserInteractionHost interactionHost)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        interactionHost.SetHosts(DialogHost, SnackbarPresenter);
        // 配色はApplicationResources.xamlの<ui:ThemesDictionary Theme="Light" />とアクセント色の
        // 静的定義(AccentFillColorDefault/Secondary/Tertiary)で固定済みのため、ここではランタイムの
        // テーマ切り替えAPI(ApplicationThemeManager.Apply/SystemThemeWatcher.Watch/
        // ApplicationAccentColorManager.Apply)を呼ばない。これらはOSのテーマ変更を監視・再適用する
        // 仕組みで、その切り替え処理(ResourceDictionaryの差し替え)自体が、バインディング経由の
        // 再描画(同期モードのラジオボタン等)をマウスを動かすまで止めてしまう副作用を持つため。
        // タイトルバーの非クライアント領域だけライト表示に合わせるため、初回1回だけ明示的に適用する。
        WindowBackgroundManager.UpdateBackground(this, ApplicationTheme.Light, WindowBackdropType.None);
        Closing += OnClosing;
    }

    /// <summary>同期実行中はウィンドウを閉じる前にキャンセル完了を待ってから終了する。</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAfterCancellation || !_viewModel.SyncWorkspace.IsSyncing) return;
        e.Cancel = true;
        await _viewModel.SyncWorkspace.CancelAndWaitAsync();
        _closeAfterCancellation = true;
        Close();
    }
}
