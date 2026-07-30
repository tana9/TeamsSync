using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
/// 実行中フラグの管理と、キャンセル/例外発生時の共通ステータス報告をViewModelへ委譲するヘルパー。
/// 継承ではなくコンポジションで各ViewModelから利用する。
/// </summary>
public sealed class BusyOperationRunner(
    INotificationService notifications,
    Action<string, bool> reportStatus,
    Action<bool>? setBusy = null)
{
    /// <summary>
    /// 処理を実行し、成功したかどうかを呼び出し元へ返す。
    /// 例外・キャンセルはここで通知まで完結させるが、後続処理を続けてよいかの判断は
    /// 呼び出し元の責務(例:サインアウト失敗時は画面状態を変更しない)のため戻り値で伝える。
    /// </summary>
    public async Task<bool> RunAsync(Func<Task> action,
        Func<Exception, (string Status, string DialogTitle)?>? handleSpecificException = null)
    {
        setBusy?.Invoke(true);
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            reportStatus("処理を中止しました", false);
            return false;
        }
        catch (Exception ex)
        {
            var specific = handleSpecificException?.Invoke(ex);
            if (specific is { } result)
            {
                reportStatus(result.Status, true);
                notifications.ShowError(ex.Message, result.DialogTitle);
            }
            else
            {
                reportStatus("エラーが発生しました。詳細はダイアログを確認してください", true);
                notifications.ShowError(ex.Message);
            }

            return false;
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }
}
