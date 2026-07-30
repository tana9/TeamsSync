using Microsoft.Extensions.DependencyInjection;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels;
using TeamsSync.Presentation.Views;
using Wpf.Ui;

namespace TeamsSync.Presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IFilePickerService, WpfFilePickerService>();
        services.AddSingleton<ISyncConfirmationService, WpfSyncConfirmationService>();
        services.AddSingleton<INotificationService, WpfNotificationService>();
        services.AddSingleton<IUserInteractionHost, WpfUserInteractionHost>();
        services.AddSingleton<IManualService, WpfManualService>();
        services.AddSingleton<TeamSelectionViewModel>();
        services.AddSingleton<MemberFileViewModel>();
        services.AddSingleton<SyncWorkspaceViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}