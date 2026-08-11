using Microsoft.Kiota.Abstractions.Authentication;

using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>Kiotaのリクエストパイプラインへ、認証サービスから取得したアクセストークンを供給する</summary>
/// <param name="authentication">Graphのアクセストークンを取得する認証サービス</param>
internal sealed class MsalAccessTokenProvider(IAuthenticationService authentication) : IAccessTokenProvider
{
    /// <summary>トークンの送信を許可するホスト(Microsoft Graphのみ)</summary>
    public AllowedHostsValidator AllowedHostsValidator { get; } = new([GraphEndpoints.Host]);

    /// <summary>リクエスト先が許可されたホストであることを確認したうえで、アクセストークンを取得する</summary>
    /// <param name="uri">トークンを送信するリクエスト先のURI</param>
    /// <param name="additionalAuthenticationContext">Kiotaから渡される追加の認証コンテキスト(未使用)</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>Microsoft Graphへのリクエストに使うアクセストークン</returns>
    public Task<string> GetAuthorizationTokenAsync(Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        return !AllowedHostsValidator.IsUrlHostValid(uri)
            ? throw new InvalidOperationException("Microsoft Graph以外のホストへトークンを送信できません。")
            : authentication.GetTokenAsync(cancellationToken: cancellationToken);
    }
}