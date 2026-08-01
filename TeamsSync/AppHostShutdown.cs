using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TeamsSync;

internal static class AppHostShutdown
{
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