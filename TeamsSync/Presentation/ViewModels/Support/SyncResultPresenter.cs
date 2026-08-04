using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels.Support;

// INotificationService自体がShowSuccess/ShowWarningをbool flagではなく別メソッドに
// 分けている方針に合わせ、ここも同じ形にする
/// <summary>同期結果と結果ログに関する通知表示を担当する。</summary>
internal sealed class SyncResultPresenter(INotificationService notifications, Func<bool> hasLog, Action openLog)
{
    /// <summary>同期が完了したことを通知する</summary>
    public void ShowSuccess(string title, string message)
    {
        if (!hasLog())
        {
            notifications.ShowSuccess(title, message);
            return;
        }

        notifications.ShowSuccessWithAction(title, WithLogSuffix(message), "同期結果CSVを開く", openLog);
    }

    /// <summary>同期が中止・部分失敗したことを通知する</summary>
    public void ShowWarning(string title, string message)
    {
        if (!hasLog())
        {
            notifications.ShowWarning(title, message);
            return;
        }

        notifications.ShowWarningWithAction(title, WithLogSuffix(message), "同期結果CSVを開く", openLog);
    }

    private static string WithLogSuffix(string message)
    {
        return $"{message} 実行ログを保存しました。";
    }
}