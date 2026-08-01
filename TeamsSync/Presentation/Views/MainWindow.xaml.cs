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
        ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.None, updateAccent: true);
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
