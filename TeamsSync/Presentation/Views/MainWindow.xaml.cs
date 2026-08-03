using System.ComponentModel;
using System.Windows.Input;

using Microsoft.Extensions.Logging;

using TeamsSync.Presentation.ViewModels;

using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TeamsSync.Presentation.Views;

/// <summary>アプリケーションのメインウィンドウ</summary>
public partial class MainWindow
{
    private readonly ILogger<MainWindow> _logger;
    private readonly MainWindowViewModel _viewModel;
    private bool _closeAfterCancellation;

    /// <summary>コンストラクター。ダイアログ・スナックバーホストを登録し、テーマを適用する</summary>
    public MainWindow(MainWindowViewModel viewModel, IContentDialogService contentDialogs,
        ISnackbarService snackbars, ILogger<MainWindow> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        DataContext = viewModel;
        contentDialogs.SetDialogHost(DialogHost);
        snackbars.SetSnackbarPresenter(SnackbarPresenter);
        // 配色はApplicationResources.xamlの<ui:ThemesDictionary Theme="Light" />とアクセント色の
        // 静的定義(AccentFillColorDefault/Secondary/Tertiary)で固定済みのため、ここではランタイムの
        // テーマ切り替えAPI(ApplicationThemeManager.Apply/SystemThemeWatcher.Watch/
        // ApplicationAccentColorManager.Apply)を呼ばない。これらはOSのテーマ変更を監視・再適用する
        // 仕組みで、その切り替え処理(ResourceDictionaryの差し替え)自体が、バインディング経由の
        // 再描画(同期モードのラジオボタン等)をマウスを動かすまで止めてしまう副作用を持つため。
        // タイトルバーの非クライアント領域だけライト表示に合わせるため、初回1回だけ明示的に適用する
        WindowBackgroundManager.UpdateBackground(this, ApplicationTheme.Light, WindowBackdropType.None);
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
    }

    /// <summary>
    ///     同期中のESCは既存のCancelCommandへ渡し、それ以外では表示中のSnackbarを閉じる
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // ContentDialogはSnackbarより前面の一時UIなので、表示中はダイアログ自身の
        // ESC処理を優先する。PreviewKeyDownで先にHandledにするとダイアログを閉じられない
        if (e.Key != Key.Escape)
        {
            return;
        }

        Snackbar? snackbar = SnackbarPresenter.Content;
        if (DecideEscapeAction(DialogHost.Content is not null,
                _viewModel.SyncWorkspace.Execution.IsRunning, snackbar?.IsShown == true) != EscapeAction.DismissSnackbar)
        {
            return;
        }

        snackbar!.IsShown = false;
        e.Handled = true;
    }

    internal static EscapeAction DecideEscapeAction(bool dialogActive, bool syncing, bool snackbarShown)
    {
        if (dialogActive)
        {
            return EscapeAction.DeferToDialog;
        }

        if (syncing)
        {
            return EscapeAction.DeferToSyncCancellation;
        }

        return snackbarShown ? EscapeAction.DismissSnackbar : EscapeAction.Unhandled;
    }

    /// <summary>同期実行中はウィンドウを閉じる前にキャンセル完了を待ってから終了する</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAfterCancellation || !_viewModel.SyncWorkspace.Execution.IsRunning)
        {
            return;
        }

        e.Cancel = true;
        try
        {
            await _viewModel.SyncWorkspace.CancelAndWaitAsync();
        }
        catch (Exception ex)
        {
            // キャンセル待機中の例外で終了処理自体が止まらないよう、ログへ記録したうえで
            // ウィンドウは通常どおり閉じる(利用者の「閉じる」操作を無言で無効化しない)
            _logger.LogError(ex, "同期キャンセルの待機中にエラーが発生しました");
        }
        finally
        {
            _closeAfterCancellation = true;
            Close();
        }
    }
}

internal enum EscapeAction
{
    Unhandled,
    DeferToDialog,
    DeferToSyncCancellation,
    DismissSnackbar
}
