using Microsoft.Extensions.DependencyInjection;

using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels;
using TeamsSync.Presentation.Views;

using Wpf.Ui;

namespace TeamsSync.Presentation;

/// <summary>
///     プレゼンテーション層(ダイアログ・ViewModel・ウィンドウ)のサービスをDIコンテナへ
///     登録するための拡張メソッドを提供する。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    ///     WPFダイアログ・通知サービスおよび各ViewModel/ウィンドウをサービスコレクションへ登録する。
    /// </summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IFilePickerService, WpfFilePickerService>();
        services.AddSingleton<ISyncConfirmationService, WpfSyncConfirmationService>();
        services.AddSingleton<IMemberInputConfirmationService, WpfMemberInputConfirmationService>();
        services.AddSingleton<INotificationService, WpfNotificationService>();
        services.AddSingleton<IManualService, WpfManualService>();
        services.AddSingleton<ISavedFileLauncher, WpfSavedFileLauncher>();
        services.AddSingleton<TeamSelectionViewModel>();
        services.AddSingleton<MemberFileViewModel>();
        services.AddSingleton<SyncWorkspaceViewModel>();
        services.AddSingleton<ManualViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}