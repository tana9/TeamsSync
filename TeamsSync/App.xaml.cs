using System.Windows;
using System.Windows.Threading;

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
///     アプリケーションのエントリポイント。Generic Hostを構築し、DI・設定・ロギングを
///     初期化したうえでメインウィンドウを表示する。
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <summary>
    ///     起動時にホストを構築し、設定の読み込みとDIコンテナの初期化を行ってからメインウィンドウを表示する。
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = e.Args, ContentRootPath = AppContext.BaseDirectory
            });
            await using Stream settings = typeof(App).Assembly.GetManifestResourceStream("appsettings.json")
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
                ValidateOnBuild = true, ValidateScopes = true
            }));

            _host = builder.Build();
            await _host.StartAsync();
            _host.Services.GetRequiredService<ILogger<App>>()
                .LogInformation("TeamsSyncを起動しました");
            _host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            string? logPath = StartupFailureLog.TryWrite(ex);
            string diagnosticHint = logPath is null
                ? ""
                : $"\n\n詳細は次のログへ記録しました。\n{logPath}";
            MessageBox.Show($"アプリケーションを起動できませんでした。設定とネットワークを確認してください。{diagnosticHint}",
                "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    // UIスレッドの未処理例外を診断できるようログへ記録する。既存の挙動(未処理のままなら
    // プロセスが終了する)は変えない(e.Handledは設定しない)。原因不明のままクラッシュログが
    // 残らない状態を避けることが目的で、任意の例外を握りつぶして実行を継続させる意図ではない。
    /// <summary>UIスレッドで発生した未処理の例外をログへ記録する。</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogCritical(e.Exception, "UIスレッドで未処理の例外が発生しました");
    }

    /// <summary>
    ///     終了時にホストを停止・破棄し、リソースを解放する。
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            IHost host = _host;
            _host = null;
            await AppHostShutdown.StopAndDisposeAsync(host, host.Services.GetService<ILogger<App>>());
        }

        base.OnExit(e);
    }
}