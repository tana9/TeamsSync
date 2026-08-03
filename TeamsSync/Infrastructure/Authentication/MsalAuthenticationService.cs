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

    // GetTokenAsyncを直列化する。並列呼び出しで_resultへの書き込みが競合し誤ったアカウントの
    // トークンが返る、または対話型サインインが複数同時に起動することを防ぐ
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private IPublicClientApplication? _app;
    private string? _configuredClientId;
    private string? _configuredTenantId;
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
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            IPublicClientApplication app = GetOrCreateApp();
            IAccount? account = (await app.GetAccountsAsync()).FirstOrDefault();

            AuthenticationResult? silentResult = null;
            if (!interactive && account is not null)
            {
                try
                {
                    silentResult = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
                }
                catch (MsalUiRequiredException)
                {
                    // サイレント取得できなかった場合は対話型サインインへフォールバックする
                }
            }

            _result = silentResult ?? await AcquireInteractiveAsync(app, account, cancellationToken);
            return _result.AccessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    /// <summary>システムブラウザーでの対話型サインインを実行する</summary>
    private async Task<AuthenticationResult> AcquireInteractiveAsync(IPublicClientApplication app,
        IAccount? account, CancellationToken cancellationToken)
    {
        logger.LogInformation("Microsoft Entra IDの対話型サインインを開始します");
        AuthenticationResult result = await app.AcquireTokenInteractive(Scopes)
            .WithAccount(account).WithPrompt(Prompt.SelectAccount)
            .WithSystemWebViewOptions(new SystemWebViewOptions
            {
                HtmlMessageSuccess = SignInSuccessHtml, HtmlMessageError = SignInErrorHtml
            })
            .ExecuteAsync(cancellationToken);
        logger.LogInformation("Microsoft Entra IDへのサインインが完了しました");
        return result;
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

        string tenantId = string.IsNullOrWhiteSpace(config.TenantId) ? "organizations" : config.TenantId;
        if (_app is not null && _configuredClientId == config.ClientId && _configuredTenantId == tenantId)
        {
            return _app;
        }

        // ClientIdが同じでもTenantIdが変わった場合、テナント制限をプロセス再起動なしで
        // 即座に反映するため、キャッシュされた古いテナント向けのアプリを再構築する。
        // 古いアプリのサインイン状態は引き継がれず、新しいテナントでの再サインインが必要になる
        _configuredClientId = config.ClientId;
        _configuredTenantId = tenantId;
        return _app = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
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
