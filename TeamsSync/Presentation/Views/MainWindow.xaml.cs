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
    private bool _cancellingBeforeClose;
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
        await OnClosingAsync(e);
    }

    private async Task OnClosingAsync(CancelEventArgs e)
    {
        switch (DecideCloseAction(_closeAfterCancellation, _cancellingBeforeClose,
                    _viewModel.SyncWorkspace.Execution.IsRunning))
        {
            case CloseAction.AllowClose:
                return;
            case CloseAction.BlockWhilePendingCancellation:
                // 既にキャンセル待機中に、再度「閉じる」操作が行われた場合。待機を二重に開始せず、
                // このクローズ要求は保留して先行する待機の完了(下のfinallyが呼ぶClose())に委ねる
                e.Cancel = true;
                return;
        }

        e.Cancel = true;
        // 待機中に再度閉じる操作をされても上のBlockWhilePendingCancellation分岐で弾けるよう、
        // 実際に待機を始める前にガードを立てる
        _cancellingBeforeClose = true;
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

    /// <summary>
    ///     同期実行中にウィンドウを閉じようとした際の対応を、現在の状態から判定する。
    ///     待機中の再クローズ操作でキャンセル待機やCloseの呼び出しが二重に走らないようにする
    /// </summary>
    internal static CloseAction DecideCloseAction(bool closeAfterCancellation, bool cancellingBeforeClose,
        bool syncing)
    {
        if (closeAfterCancellation)
        {
            // キャンセル完了後、自身のClose()呼び出しが再度発火させたClosingイベント。素通りさせる
            return CloseAction.AllowClose;
        }

        if (cancellingBeforeClose)
        {
            return CloseAction.BlockWhilePendingCancellation;
        }

        return syncing ? CloseAction.StartCancelAndWait : CloseAction.AllowClose;
    }
}

internal enum EscapeAction
{
    Unhandled,
    DeferToDialog,
    DeferToSyncCancellation,
    DismissSnackbar
}

internal enum CloseAction
{
    AllowClose,
    StartCancelAndWait,
    BlockWhilePendingCancellation
}