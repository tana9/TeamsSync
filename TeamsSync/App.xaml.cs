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

using ThemedMessageBox = Wpf.Ui.Controls.MessageBox;

namespace TeamsSync;

/// <summary>
///     アプリケーションのエントリポイント。Generic Hostを構築し、DI・設定・ロギングを
///     初期化したうえでメインウィンドウを表示する
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    // 起動処理は2段階に分けている。
    // ①設定読込・DI構築(BuildHostAsync): WPFのXAML/リソースを一切読み込まないため、失敗しても
    //   WPF-UIのテーマ付きダイアログを安全に表示できる。原因が既知の場合はStartupConfigurationException
    //   として区別し、より具体的な案内を出す。
    // ②MainWindow.Show(): ここで初めてXAML/リソースが読み込まれる。この段階の失敗は、
    //   テーマ付きダイアログ自体の描画に必要なリソースが壊れている可能性があるため、
    //   何にも依存しないネイティブのMessageBoxで最後の砦として報告する
    /// <summary>
    ///     起動時にホストを構築し、設定の読み込みとDIコンテナの初期化を行ってからメインウィンドウを表示する
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        await OnStartupAsync(e);
    }

    private async Task OnStartupAsync(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        IHost host;
        try
        {
            host = await BuildHostAsync(e);
        }
        catch (StartupConfigurationException ex)
        {
            StartupFailureLog.TryWrite(ex);
            // 起動失敗時に表示するダイアログがこのアプリで最初のウィンドウになるため、既定の
            // ShutdownMode(OnLastWindowClose)のままだと、利用者がダイアログを閉じた時点で
            // 暗黙のシャットダウン(終了コード0)が走り、直後のShutdown(-1)と競合しうる。
            // 失敗経路でだけ明示シャットダウンへ切り替え、終了コードを確実に-1にする
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await ShowThemedStartupErrorAsync(ex.Message, "起動時の設定エラー");
            Shutdown(-1);
            return;
        }
        catch (Exception ex)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await ShowThemedStartupErrorAsync(
                $"アプリケーションを起動できませんでした。設定とネットワークを確認してください。{BuildDiagnosticHint(ex)}", "起動エラー");
            Shutdown(-1);
            return;
        }

        try
        {
            _host = host;
            host.Services.GetRequiredService<ILogger<App>>()
                .LogInformation("TeamsSyncを起動しました");
            host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            MessageBox.Show(
                $"アプリケーションを起動できませんでした。設定とネットワークを確認してください。{BuildDiagnosticHint(ex)}",
                "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <summary>診断ログの保存を試み、利用者向けメッセージに添えるヒント文を組み立てる</summary>
    private static string BuildDiagnosticHint(Exception ex)
    {
        string? logPath = StartupFailureLog.TryWrite(ex);
        return logPath is null ? "" : $"\n\n詳細は次のログへ記録しました。\n{logPath}";
    }

    /// <summary>
    ///     Generic Hostを構築・起動する。appsettings.jsonの埋め込みリソースが見つからない場合は、
    ///     原因が既知であることを示す<see cref="StartupConfigurationException" />をスローする
    /// </summary>
    private static async Task<IHost> BuildHostAsync(StartupEventArgs e)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args, ContentRootPath = AppContext.BaseDirectory
        });
        await using Stream settings = typeof(App).Assembly.GetManifestResourceStream("appsettings.json")
                                      ?? throw new StartupConfigurationException(
                                          "埋め込み設定 appsettings.json を読み込めません。インストールし直してください。");
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

        IHost host = builder.Build();
        try
        {
            await host.StartAsync();
            return host;
        }
        catch
        {
            // StartAsync失敗時、hostへの参照をこのメソッドの外へ一切渡さないため、
            // ここで破棄しないとSerilog等が開いたファイルハンドルがリークする
            await AppHostShutdown.StopAndDisposeAsync(host, null);
            throw;
        }
    }

    /// <summary>
    ///     WPF-UIのテーマ付きダイアログ(独立ウィンドウ)でエラーを表示する。
    ///     ContentDialogとは異なりContentDialogHostを必要としないため、MainWindow表示前でも使える
    /// </summary>
    private static async Task ShowThemedStartupErrorAsync(string message, string title)
    {
        ThemedMessageBox dialog = new() { Title = title, Content = message, CloseButtonText = "閉じる" };
        await dialog.ShowDialogAsync();
    }

    // UIスレッドの未処理例外を診断できるようログへ記録する。既存の挙動(未処理のままなら
    // プロセスが終了する)は変えない(e.Handledは設定しない)。原因不明のままクラッシュログが
    // 残らない状態を避けることが目的で、任意の例外を握りつぶして実行を継続させる意図ではない
    /// <summary>UIスレッドで発生した未処理の例外をログへ記録する</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogCritical(e.Exception, "UIスレッドで未処理の例外が発生しました");
    }

    /// <summary>
    ///     終了時にホストを停止・破棄し、リソースを解放する
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        await OnExitAsync(e);
    }

    private async Task OnExitAsync(ExitEventArgs e)
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
