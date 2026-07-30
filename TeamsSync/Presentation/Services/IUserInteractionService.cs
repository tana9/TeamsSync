using TeamsSync.Domain.Teams;
using Wpf.Ui.Controls;

namespace TeamsSync.Presentation.Services;

public sealed record SyncConfirmation(SyncPlan Plan, string FileName, string InputSummary = "");

public interface IFilePickerService
{
    string? PickMemberFile(string? initialDirectory);
    string? PickResultFile(string? initialDirectory, string teamName);
}

public interface ISyncConfirmationService
{
    Task<bool> ConfirmSyncAsync(SyncConfirmation confirmation,
        CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    void ShowSuccess(string title, string message);
    void ShowWarning(string title, string message);

    // onClosed: エラーダイアログを閉じた後に実行するコールバック(省略可)。
    // ShowErrorはダイアログの表示完了を待たずに呼び出し元へ戻るため、ダイアログが閉じてから
    // フォーカスを移動させたい場合などはここで受け取る。
    void ShowError(string message, string title = "エラー", Action? onClosed = null);
}

public interface IUserInteractionHost
{
    void SetHosts(ContentDialogHost dialogHost, SnackbarPresenter snackbarPresenter);
}

public interface IManualService
{
    void OpenManual();
}
