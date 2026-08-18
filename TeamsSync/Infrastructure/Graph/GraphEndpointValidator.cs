namespace TeamsSync.Infrastructure.Graph;

/// <summary>Bearerトークンを送信できるMicrosoft Graphエンドポイントを制限する</summary>
internal static class GraphEndpointValidator
{
    /// <summary>URIがMicrosoft Graphの想定エンドポイント(https・既定ポート・ユーザー情報なし)であることを検証する</summary>
    /// <param name="uri">検証するURI</param>
    /// <exception cref="InvalidDataException">許可されていないURIの場合</exception>
    public static void Validate(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.Equals(uri.Host, GraphEndpoints.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException("許可されていないMicrosoft Graphエンドポイントです。");
        }
    }
}