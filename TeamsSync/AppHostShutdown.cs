using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TeamsSync;

internal static class AppHostShutdown
{
    /// <summary>
    ///     同期的なWPF終了イベントからホストの停止・破棄を完了まで待つ。
    ///     UI同期コンテキストを引き継いだ非同期継続とのデッドロックを避けるため、処理は
    ///     スレッドプール上で実行する
    /// </summary>
    internal static void StopAndDispose(IHost host, ILogger? logger,
        TimeSpan? timeout = null)
    {
        Task.Run(() => StopAndDisposeAsync(host, logger, timeout)).GetAwaiter().GetResult();
    }

    internal static async Task StopAndDisposeAsync(IHost host, ILogger? logger,
        TimeSpan? timeout = null)
    {
        try
        {
            logger?.LogInformation("TeamsSyncを終了します");
            await host.StopAsync(timeout ?? TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "TeamsSyncの終了処理に失敗しました");
        }
        finally
        {
            host.Dispose();
        }
    }
}
