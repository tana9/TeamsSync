using Microsoft.Kiota.Abstractions.Authentication;

using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>Kiotaのリクエストパイプラインへ、認証サービスから取得したアクセストークンを供給する</summary>
/// <param name="authentication">Graphのアクセストークンを取得する認証サービス</param>
internal sealed class MsalAccessTokenProvider(IAuthenticationService authentication) : IAccessTokenProvider
{
    /// <summary>トークンの送信を許可するホスト(Microsoft Graphのみ)</summary>
    public AllowedHostsValidator AllowedHostsValidator { get; } = new([GraphEndpoints.Host]);

    // KiotaのHttpClientRequestAdapterは、このメソッドをトランスポート層(GraphSdkTransportHandler)より
    // 先に呼び出す。送信直前(GraphSdkTransportHandler)だけで検証すると、不正なURLに対しても
    // トークン取得(MSALの対話サインインを誘発しうる処理)が先に走ってしまう。AllowedHostsValidatorは
    // ホスト名しか見ずポート・スキーム・ユーザー情報を検証しないため、ここでは同じ許可基準を持つ
    // GraphEndpointValidatorをトークン取得より前に呼び、不正なURLの場合はトークンを取得せず即座に拒否する。
    // URL検証はこのメソッドだけで行い、GraphSdkTransportHandler側では重複させていない
    /// <summary>リクエスト先が許可されたエンドポイントであることを確認したうえで、アクセストークンを取得する</summary>
    /// <param name="uri">トークンを送信するリクエスト先のURI</param>
    /// <param name="additionalAuthenticationContext">Kiotaから渡される追加の認証コンテキスト(未使用)</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>Microsoft Graphへのリクエストに使うアクセストークン</returns>
    public Task<string> GetAuthorizationTokenAsync(Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        GraphEndpointValidator.Validate(uri);
        return authentication.GetTokenAsync(cancellationToken: cancellationToken);
    }
}