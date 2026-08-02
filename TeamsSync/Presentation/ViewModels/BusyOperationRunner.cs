using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     実行中フラグの管理と、キャンセル/例外発生時の共通ステータス報告をViewModelへ委譲するヘルパー。
///     継承ではなくコンポジションで各ViewModelから利用する
/// </summary>
public sealed class BusyOperationRunner(
    INotificationService notifications,
    Action<string, bool> reportStatus,
    Action<bool>? setBusy = null)
{
    private const string DefaultErrorMessage = "エラーが発生しました。通知の「詳細をコピー」から内容を確認できます";
    private const string CancellationMessage = "処理を中止しました";

    /// <summary>
    ///     戻り値のない処理を実行し、成功したかどうかを返す。
    ///     例外・キャンセルはここで通知まで完結させるが、後続処理を続けてよいかの判断は
    ///     呼び出し元の責務(例:サインアウト失敗時は画面状態を変更しない)のため戻り値で伝える
    /// </summary>
    /// <param name="action">実行する非同期処理</param>
    /// <param name="handleSpecificException">
    ///     例外を固有のステータス通知へ変換する関数。既定のエラー処理を使う場合はnull
    /// </param>
    /// <param name="manageBusyState">
    ///     falseの場合、setBusyの呼び出しを行わない。既にこのインスタンスのRunAsyncで
    ///     処理中状態になっている呼び出し元から入れ子で呼ぶ場合に使う。同じインスタンスの
    ///     RunAsyncを入れ子でtrueのまま呼ぶと、内側の完了時にsetBusy(false)が呼ばれて
    ///     外側の処理がまだ実行中にもかかわらずIsBusyが早期にfalseへ戻ってしまう
    ///     (画面が実行中に一瞬再操作可能になる不具合の原因になる)
    /// </param>
    /// <param name="cancellationToken">利用者または呼び出し元から要求されたキャンセルを識別するトークン</param>
    public async Task<bool> RunAsync(Func<Task> action,
        Func<Exception, SpecificExceptionResult?>? handleSpecificException = null,
        bool manageBusyState = true, CancellationToken cancellationToken = default)
    {
        if (manageBusyState)
        {
            setBusy?.Invoke(true);
        }

        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reportStatus(CancellationMessage, false);
            return false;
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ex, handleSpecificException);
            return false;
        }
        finally
        {
            if (manageBusyState)
            {
                setBusy?.Invoke(false);
            }
        }
    }

    /// <summary>
    ///     戻り値のある処理を実行し、成功した場合はその結果を、失敗した場合は default(T) を返す
    /// </summary>
    /// <param name="action">実行する非同期処理</param>
    /// <param name="handleSpecificException">
    ///     例外を固有のステータス通知へ変換する関数。既定のエラー処理を使う場合はnull
    /// </param>
    /// <param name="manageBusyState">
    ///     <see cref="RunAsync(Func{Task}, Func{Exception, SpecificExceptionResult?}?, bool, CancellationToken)" />を参照
    /// </param>
    /// <param name="cancellationToken">利用者または呼び出し元から要求されたキャンセルを識別するトークン</param>
    public async Task<T?> RunAsync<T>(Func<Task<T>> action,
        Func<Exception, SpecificExceptionResult?>? handleSpecificException = null,
        bool manageBusyState = true, CancellationToken cancellationToken = default)
    {
        if (manageBusyState)
        {
            setBusy?.Invoke(true);
        }

        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reportStatus(CancellationMessage, false);
            return default;
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ex, handleSpecificException);
            return default;
        }
        finally
        {
            if (manageBusyState)
            {
                setBusy?.Invoke(false);
            }
        }
    }

    private async Task HandleExceptionAsync(Exception ex,
        Func<Exception, SpecificExceptionResult?>? handleSpecificException)
    {
        SpecificExceptionResult? specific = handleSpecificException?.Invoke(ex);
        if (specific is not null)
        {
            reportStatus(specific.Status, true);
            if (specific.IsCritical)
            {
                await notifications.ShowCriticalErrorAsync(ex.Message, specific.DialogTitle ?? "エラー");
            }
            else
            {
                await notifications.ShowErrorAsync(ex.Message, specific.DialogTitle ?? "エラー");
            }
        }
        else
        {
            reportStatus(DefaultErrorMessage, true);
            await notifications.ShowErrorAsync(ex.Message);
        }
    }

    /// <summary>特定の例外を処理した際、ViewModelへ報告するステータスとダイアログのタイトルを保持する</summary>
    public record SpecificExceptionResult(string Status, string? DialogTitle = null, bool IsCritical = false);
}
