using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TeamsSync.Application;
using TeamsSync.Infrastructure;
using TeamsSync.Infrastructure.Logging;
using TeamsSync.Presentation;
using TeamsSync.Presentation.Views;

namespace TeamsSync;

/// <summary>
/// アプリケーションのエントリポイント。Generic Hostを構築し、DI・設定・ロギングを
/// 初期化したうえでメインウィンドウを表示する。
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <summary>
    /// 起動時にホストを構築し、設定の読み込みとDIコンテナの初期化を行ってからメインウィンドウを表示する。
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = e.Args,
                ContentRootPath = AppContext.BaseDirectory
            });
            await using var settings = typeof(App).Assembly.GetManifestResourceStream("appsettings.json")
                                       ?? throw new InvalidOperationException("埋め込み設定 appsettings.json を読み込めません。");
            builder.Configuration
                .AddJsonStream(settings)
                .AddEnvironmentVariables("TEAMSSYNC_")
                .AddCommandLine(e.Args);
            builder.Services.AddAuditLogging(builder.Configuration);
            builder.Logging.ClearProviders();
            builder.Services
                .AddApplication()
                .AddInfrastructure(builder.Configuration)
                .AddPresentation();
            builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            }));

            _host = builder.Build();
            await _host.StartAsync();
            _host.Services.GetRequiredService<ILogger<App>>()
                .LogInformation("TeamsSyncを起動しました");
            _host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"アプリケーションを起動できませんでした。\n\n{ex.Message}",
                "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <summary>
    /// 終了時にホストを停止・破棄し、リソースを解放する。
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            var host = _host;
            _host = null;
            await AppHostShutdown.StopAndDisposeAsync(host, host.Services.GetService<ILogger<App>>());
        }

        base.OnExit(e);
    }
}
