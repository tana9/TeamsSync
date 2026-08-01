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
        // IMemberInputConfirmationServiceはAddPresentation()が登録する本番のContentDialog実装(WpfMemberInputConfirmationService)を
        // そのまま使う。シナリオ自動実行(ScenarioWindow)がダイアログ待ちで止まらないよう、
        // ISyncConfirmationServiceのみデモ用の自動承認スタブへ差し替える。
        builder.Services
            .AddSingleton<DemoConfirmationService>()
            .AddSingleton<TeamsSync.Presentation.Services.ISyncConfirmationService>(provider => provider.GetRequiredService<DemoConfirmationService>());

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
