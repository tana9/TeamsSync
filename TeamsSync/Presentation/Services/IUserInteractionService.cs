using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.Services;

/// <summary>同期実行前の最終確認ダイアログに表示する内容をまとめたもの。</summary>
/// <param name="Plan">確認対象の同期プラン。</param>
/// <param name="FileName">入力元のファイル名。</param>
/// <param name="InputSummary">入力内容の要約テキスト。</param>
public sealed record SyncConfirmation(SyncPlan Plan, string FileName, string InputSummary = "");

/// <summary>メンバーリストファイルの選択ダイアログを表示する。</summary>
public interface IFilePickerService
{
    /// <summary>メンバーリストファイル(CSV/Excel)を選択するダイアログを表示する。</summary>
    string? PickMemberFile(string? initialDirectory);
}

/// <summary>同期実行前の最終確認ダイアログを表示する。</summary>
public interface ISyncConfirmationService
{
    /// <summary>確認ダイアログを表示し、ユーザーが同期の実行を選んだかどうかを返す。</summary>
    Task<bool> ConfirmSyncAsync(SyncConfirmation confirmation,
        CancellationToken cancellationToken = default);
}

/// <summary>現在のチームメンバーを既存の入力へ上書きする前に確認する。</summary>
public interface IMemberInputConfirmationService
{
    /// <summary>既存入力を置き換えてよいか確認し、利用者が続行を選んだ場合にtrueを返す。</summary>
    Task<bool> ConfirmReplaceMemberInputAsync(string teamName, int memberCount,
        CancellationToken cancellationToken = default);
}

/// <summary>成功・警告・エラーをユーザーへ通知する。</summary>
public interface INotificationService
{
    /// <summary>成功通知(スナックバー)を表示する。</summary>
    void ShowSuccess(string title, string message);

    /// <summary>利用者が実行できるアクション付きの成功通知を表示する。</summary>
    void ShowSuccessWithAction(string title, string message, string actionText, Action action)
    {
        ShowSuccess(title, message);
    }

    /// <summary>警告通知(スナックバー)を表示する。</summary>
    void ShowWarning(string title, string message);

    /// <summary>利用者が実行できるアクション付きの警告通知を表示する。</summary>
    void ShowWarningWithAction(string title, string message, string actionText, Action action)
    {
        ShowWarning(title, message);
    }

    /// <summary>復旧可能なエラーを、詳細コピー操作付きの持続表示通知として表示する。</summary>
    Task ShowErrorAsync(string message, string title = "エラー", Action? onClosed = null);

    /// <summary>操作を続行できない重大なエラーを、閉じるまで残るダイアログとして表示する。</summary>
    Task ShowCriticalErrorAsync(string message, string title = "エラー", Action? onClosed = null)
    {
        return ShowErrorAsync(message, title, onClosed);
    }
}

/// <summary>利用者向けマニュアルを表示する。</summary>
public interface IManualService
{
    /// <summary>埋め込みマニュアルを既定のブラウザーで開く。</summary>
    void OpenManual();
}

/// <summary>保存済みファイルをOSで関連付けられたアプリケーションから開く。</summary>
public interface ISavedFileLauncher
{
    /// <summary>指定したファイルを既定のアプリケーションで開く。</summary>
    void Open(string path);
}
