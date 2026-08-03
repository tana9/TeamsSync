using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels.Support;

/// <summary>同期結果と結果ログに関する通知表示を担当する。</summary>
internal sealed class SyncResultPresenter(INotificationService notifications, Func<bool> hasLog, Action openLog)
{
    public void Show(bool warning, string title, string message)
    {
        if (!hasLog())
        {
            if (warning)
            {
                notifications.ShowWarning(title, message);
            }
            else
            {
                notifications.ShowSuccess(title, message);
            }

            return;
        }

        string messageWithLog = $"{message} 実行ログを保存しました。";
        if (warning)
        {
            notifications.ShowWarningWithAction(title, messageWithLog, "同期結果CSVを開く", openLog);
        }
        else
        {
            notifications.ShowSuccessWithAction(title, messageWithLog, "同期結果CSVを開く", openLog);
        }
    }
}