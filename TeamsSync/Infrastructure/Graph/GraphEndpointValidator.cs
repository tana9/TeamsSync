namespace TeamsSync.Infrastructure.Graph;

/// <summary>Bearerトークンを送信できるMicrosoft Graphエンドポイントを制限する</summary>
internal static class GraphEndpointValidator
{
    public static void Validate(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.Equals(uri.Host, GraphEndpoints.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException("Graph APIの応答に許可されていないURLが含まれています。");
        }
    }
}