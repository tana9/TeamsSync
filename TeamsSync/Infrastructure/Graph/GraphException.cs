using System.Net;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>Microsoft Graph API呼び出しが失敗したことを表す例外</summary>
public sealed class GraphException(
    HttpStatusCode statusCode,
    string message,
    string? requestId = null,
    string? clientRequestId = null) : Exception(message)
{
    /// <summary>レスポンスのHTTPステータスコード</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Graph側の診断用リクエストID(取得できた場合)</summary>
    public string? RequestId { get; } = requestId;

    /// <summary>クライアント側で発行した診断用リクエストID</summary>
    public string? ClientRequestId { get; } = clientRequestId;
}
