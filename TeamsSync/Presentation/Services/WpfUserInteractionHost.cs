using Wpf.Ui;
using Wpf.Ui.Controls;

namespace TeamsSync.Presentation.Services;

/// <summary>WPF-UIのダイアログ・スナックバー表示先ホストをメインウィンドウへ結び付ける。</summary>
public sealed class WpfUserInteractionHost(
    IContentDialogService contentDialogs,
    ISnackbarService snackbars) : IUserInteractionHost
{
    public void SetHosts(ContentDialogHost dialogHost, SnackbarPresenter snackbarPresenter)
    {
        contentDialogs.SetDialogHost(dialogHost);
        snackbars.SetSnackbarPresenter(snackbarPresenter);
    }
}
