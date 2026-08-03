using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

using System.Reflection;

using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Authentication;

/// <summary>
///     MSAL.NETを用いてMicrosoft Entra IDへのサインイン・サインアウトとアクセストークン取得を行う
/// </summary>
public sealed class MsalAuthenticationService(
    IOptionsMonitor<EntraOptions> options,
    ILogger<MsalAuthenticationService> logger) : IAuthenticationService
{
    private static readonly string[] Scopes =
    [
        "User.Read", "User.ReadBasic.All", "Team.ReadBasic.All",
        "TeamMember.Read.All", "TeamMember.ReadWriteNonOwnerRole.All"
    ];

    private static readonly string SignInSuccessHtml = LoadEmbeddedHtml("Authentication.SignInSuccess.html");
    private static readonly string SignInErrorHtml = LoadEmbeddedHtml("Authentication.SignInError.html");

    private IPublicClientApplication? _app;
    private string? _configuredClientId;
    private AuthenticationResult? _result;

    /// <summary>サインイン中のユーザー名(未サインインの場合はnull)</summary>
    public string? UserName => _result?.Account?.Username;

    /// <summary>サインイン中のテナントID(未サインインの場合はnull)</summary>
    public string? TenantId => _result?.TenantId;

    /// <summary>
    ///     アクセストークンを取得する。サイレント取得できない場合、またはinteractiveが
    ///     指定された場合は、システムブラウザーでの対話型サインインへフォールバックする
    /// </summary>
    public async Task<string> GetTokenAsync(bool interactive = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPublicClientApplication app = GetOrCreateApp();
        IAccount? account = (await app.GetAccountsAsync()).FirstOrDefault();
        try
        {
            if (!interactive && account is not null)
            {
                _result = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
            }
            else
            {
                throw new MsalUiRequiredException("interactive_required", "Interactive sign-in required.");
            }
        }
        catch (MsalUiRequiredException)
        {
            logger.LogInformation("Microsoft Entra IDの対話型サインインを開始します");
            _result = await app.AcquireTokenInteractive(Scopes)
                .WithAccount(account).WithPrompt(Prompt.SelectAccount)
                .WithSystemWebViewOptions(new SystemWebViewOptions
                {
                    HtmlMessageSuccess = SignInSuccessHtml, HtmlMessageError = SignInErrorHtml
                })
                .ExecuteAsync(cancellationToken);
            logger.LogInformation("Microsoft Entra IDへのサインインが完了しました");
        }

        return _result.AccessToken;
    }

    /// <summary>
    ///     MSALにキャッシュされているすべてのアカウントを削除し、サインイン状態をクリアする
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_app is not null)
        {
            foreach (IAccount account in await _app.GetAccountsAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _app.RemoveAsync(account);
            }
        }

        _result = null;
        logger.LogInformation("Microsoft Entra IDからサインアウトしました");
    }

    /// <summary>
    ///     現在の設定に対応する<see cref="IPublicClientApplication" />を取得または作成する。
    ///     ClientIdが未設定の場合は<see cref="AuthenticationConfigurationException" />をスローする
    /// </summary>
    private IPublicClientApplication GetOrCreateApp()
    {
        EntraOptions config = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(config.ClientId) || config.ClientId == "YOUR-APPLICATION-CLIENT-ID")
        {
            throw new AuthenticationConfigurationException(
                "Entra IDのアプリ設定がありません。ClientIdを設定ファイル、環境変数 TEAMSSYNC_Entra__ClientId、または起動引数 --Entra:ClientId で指定してください。TenantIdは省略できます。");
        }

        if (_app is not null && _configuredClientId == config.ClientId)
        {
            return _app;
        }

        _configuredClientId = config.ClientId;
        return _app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic,
                string.IsNullOrWhiteSpace(config.TenantId) ? "organizations" : config.TenantId)
            .WithDefaultRedirectUri().Build();
    }

    private static string LoadEmbeddedHtml(string resourceName)
    {
        Assembly assembly = typeof(MsalAuthenticationService).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
                              ?? throw new InvalidOperationException($"埋め込みHTMLを読み込めません: {resourceName}");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
