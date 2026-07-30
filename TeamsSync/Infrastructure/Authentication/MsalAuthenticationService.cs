using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Authentication;

public sealed class MsalAuthenticationService(
    IOptionsMonitor<EntraOptions> options,
    ILogger<MsalAuthenticationService> logger) : IAuthenticationService
{
    private static readonly string[] Scopes =
    [
        "User.Read", "User.ReadBasic.All", "Team.ReadBasic.All",
        "TeamMember.Read.All", "TeamMember.ReadWriteNonOwnerRole.All"
    ];

    private const string SignInResultPageStyle = """
        <style>
            body { font-family: "Yu Gothic UI", "Segoe UI", "Meiryo", system-ui, sans-serif;
                   display: flex; align-items: center; justify-content: center;
                   height: 100vh; margin: 0; background: #F5F5FA; color: #1F1F1F; }
            .card { text-align: center; padding: 40px 48px; border-radius: 12px;
                    background: white; box-shadow: 0 4px 16px rgba(0,0,0,0.08); }
            .icon { font-size: 40px; }
            h1 { font-size: 18px; margin: 12px 0 4px; }
            p { font-size: 14px; color: #616161; margin: 0; }
        </style>
        """;

    private static readonly string SignInSuccessHtml = $$"""
        <!doctype html><html lang="ja"><head><meta charset="utf-8" />{{SignInResultPageStyle}}</head>
        <body><div class="card">
            <div class="icon" style="color:#5B5FC7">&#10003;</div>
            <h1>サインインが完了しました</h1>
            <p>このウィンドウを閉じてTeamsSyncに戻ってください。</p>
        </div></body></html>
        """;

    private static readonly string SignInErrorHtml = $$"""
        <!doctype html><html lang="ja"><head><meta charset="utf-8" />{{SignInResultPageStyle}}</head>
        <body><div class="card">
            <div class="icon" style="color:#C42B1C">&#10005;</div>
            <h1>サインインに失敗しました</h1>
            <p>このウィンドウを閉じてTeamsSyncに戻り、もう一度お試しください。</p>
        </div></body></html>
        """;

    private IPublicClientApplication? _app;
    private string? _configuredClientId;
    private AuthenticationResult? _result;

    public string? UserName => _result?.Account?.Username;
    public string? TenantId => _result?.TenantId;

    public async Task<string> GetTokenAsync(bool interactive = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var app = GetOrCreateApp();
        var account = (await app.GetAccountsAsync()).FirstOrDefault();
        try
        {
            if (!interactive && account is not null)
                _result = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
            else
                throw new MsalUiRequiredException("interactive_required", "Interactive sign-in required.");
        }
        catch (MsalUiRequiredException)
        {
            logger.LogInformation("Microsoft Entra IDの対話型サインインを開始します");
            _result = await app.AcquireTokenInteractive(Scopes)
                .WithAccount(account).WithPrompt(Prompt.SelectAccount)
                .WithSystemWebViewOptions(new SystemWebViewOptions
                {
                    HtmlMessageSuccess = SignInSuccessHtml,
                    HtmlMessageError = SignInErrorHtml
                })
                .ExecuteAsync(cancellationToken);
            logger.LogInformation("Microsoft Entra IDへのサインインが完了しました");
        }

        return _result.AccessToken;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_app is not null)
            foreach (var account in await _app.GetAccountsAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _app.RemoveAsync(account);
            }

        _result = null;
        logger.LogInformation("Microsoft Entra IDからサインアウトしました");
    }

    private IPublicClientApplication GetOrCreateApp()
    {
        var config = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(config.ClientId) || config.ClientId == "YOUR-APPLICATION-CLIENT-ID")
            throw new AuthenticationConfigurationException(
                "Entra IDのアプリ設定がありません。ClientIdを設定ファイル、環境変数 TEAMSSYNC_Entra__ClientId、または起動引数 --Entra:ClientId で指定してください。TenantIdは省略できます。");
        if (_app is not null && _configuredClientId == config.ClientId) return _app;
        _configuredClientId = config.ClientId;
        return _app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic,
                string.IsNullOrWhiteSpace(config.TenantId) ? "organizations" : config.TenantId)
            .WithDefaultRedirectUri().Build();
    }
}