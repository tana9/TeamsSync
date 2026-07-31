using TeamsSync.Domain.Teams;
using Wpf.Ui.Controls;

namespace TeamsSync.Presentation.Services;

/// <summary>同期実行前の最終確認ダイアログに表示する内容をまとめたもの。</summary>
/// <param name="Plan">確認対象の同期プラン。</param>
/// <param name="FileName">入力元のファイル名。</param>
/// <param name="InputSummary">入力内容の要約テキスト。</param>
public sealed record SyncConfirmation(SyncPlan Plan, string FileName, string InputSummary = "");

/// <summary>メンバーリストファイル・同期結果ファイルの選択ダイアログを表示する。</summary>
public interface IFilePickerService
{
    /// <summary>メンバーリストファイル(CSV/Excel)を選択するダイアログを表示する。</summary>
    string? PickMemberFile(string? initialDirectory);

    /// <summary>同期結果の保存先ファイルを選択するダイアログを表示する。</summary>
    string? PickResultFile(string? initialDirectory, string teamName);
}

/// <summary>同期実行前の最終確認ダイアログを表示する。</summary>
public interface ISyncConfirmationService
{
    /// <summary>確認ダイアログを表示し、ユーザーが同期の実行を選んだかどうかを返す。</summary>
    Task<bool> ConfirmSyncAsync(SyncConfirmation confirmation,
        CancellationToken cancellationToken = default);
}

/// <summary>成功・警告・エラーをユーザーへ通知する。</summary>
public interface INotificationService
{
    /// <summary>成功通知(スナックバー)を表示する。</summary>
    void ShowSuccess(string title, string message);

    /// <summary>警告通知(スナックバー)を表示する。</summary>
    void ShowWarning(string title, string message);

    // onClosed: エラーダイアログを閉じた後に実行するコールバック(省略可)。
    // ShowErrorはダイアログの表示完了を待たずに呼び出し元へ戻るため、ダイアログが閉じてから
    // フォーカスを移動させたい場合などはここで受け取る。
    /// <summary>エラーダイアログを表示する。</summary>
    void ShowError(string message, string title = "エラー", Action? onClosed = null);
}

/// <summary>ダイアログ・スナックバーの表示先ホストをWPFのコンテンツホストへ結び付ける。</summary>
public interface IUserInteractionHost
{
    /// <summary>ダイアログホストとスナックバー表示先を登録する。</summary>
    void SetHosts(ContentDialogHost dialogHost, SnackbarPresenter snackbarPresenter);
}

/// <summary>利用者向けマニュアルを表示する。</summary>
public interface IManualService
{
    /// <summary>埋め込みマニュアルを既定のブラウザーで開く。</summary>
    void OpenManual();
}
