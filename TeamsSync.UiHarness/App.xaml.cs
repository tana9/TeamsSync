using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamsSync.Application;
using TeamsSync.Application.Abstractions;
using TeamsSync.Infrastructure.Files;
using TeamsSync.Presentation;
using TeamsSync.Presentation.ViewModels;
using TeamsSync.Presentation.Views;

namespace TeamsSync.UiHarness;

/// <summary>実テナントへ接続せず、本番UIをデモデータで確認する開発専用アプリ。</summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var builder = Host.CreateApplicationBuilder(e.Args);
        builder.Services
            .AddApplication()
            .AddSingleton<IAuthenticationService, DemoAuthenticationService>()
            .AddSingleton<DemoTeamsGateway>()
            .AddSingleton<ITeamsGateway>(provider => provider.GetRequiredService<DemoTeamsGateway>())
            .AddSingleton<IMemberListReader, MemberListReader>()
            .AddSingleton<IMemberTextParser, MemberTextParser>()
            .AddSingleton<ISyncResultWriter, SyncResultWriter>()
            .AddSingleton<IUserPreferences, DemoUserPreferences>()
            .AddPresentation();
        // ISyncConfirmationService/IMemberInputConfirmationServiceともAddPresentation()が登録する
        // 本番のContentDialog実装をそのまま使う。「同期成功結果」「同期一部失敗」シナリオ(Execute: true)は
        // ExecuteSyncCommand実行時に確認ダイアログが表示され、利用者が応答するまで待機する。

        _host = builder.Build();
        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();

        // 認証画面を経由せず、起動直後からチーム選択以降のUIを確認できる状態にする。
        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        await viewModel.SignIn.SignInCommand.ExecuteAsync(null);
        var scenarios = new ScenarioWindow(viewModel, _host.Services.GetRequiredService<DemoTeamsGateway>())
        {
            Owner = window
        };
        scenarios.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
