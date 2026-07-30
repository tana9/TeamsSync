using System.ComponentModel;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TeamsSync.Presentation.Views;

public partial class MainWindow
{
    private readonly MainWindowViewModel _viewModel;
    private bool _closeAfterCancellation;

    public MainWindow(MainWindowViewModel viewModel, IUserInteractionHost interactionHost)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        interactionHost.SetHosts(DialogHost, SnackbarPresenter);
        ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.None, updateAccent: true);
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAfterCancellation || !_viewModel.SyncWorkspace.IsSyncing) return;
        e.Cancel = true;
        await _viewModel.SyncWorkspace.CancelAndWaitAsync();
        _closeAfterCancellation = true;
        Close();
    }
}