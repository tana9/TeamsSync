using System.Net;

using Microsoft.Extensions.Logging;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>SDK要求を既存の名前付きHttpClientへ転送し、診断情報と例外形式を維持する。</summary>
internal sealed partial class GraphSdkTransportHandler(HttpClient transport, ILogger<GraphHttpClient> logger)
    : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        GraphEndpointValidator.Validate(request.RequestUri);
        string clientRequestId = Guid.NewGuid().ToString();
        bool expectedNotFound = request.Headers.Remove("x-teams-sync-expected-not-found");
        request.Headers.TryAddWithoutValidation("client-request-id", clientRequestId);
        request.Headers.TryAddWithoutValidation("return-client-request-id", "true");
        using HttpRequestMessage forwarded = await CloneAsync(request, cancellationToken);
        HttpResponseMessage response = await transport.SendAsync(forwarded, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        HttpStatusCode status = response.StatusCode;
        string? requestId = Header(response, "request-id");
        string returnedClientRequestId = Header(response, "client-request-id") ?? clientRequestId;
        if (expectedNotFound && status == HttpStatusCode.NotFound)
        {
            LogFallbackToSearch(logger, status, requestId, returnedClientRequestId);
        }
        else if (logger.IsEnabled(LogLevel.Error))
        {
            // DiagnosticSummary(body)はJSON解析を伴うため、Errorログが無効な場合は評価しない(CA1873)。
            // [LoggerMessage]は内部でIsEnabledを判定するが、呼び出し側の引数(DiagnosticSummaryの呼び出し)
            // 自体はC#の評価順序上どのみ実行されてしまうため、このガードは生成メソッド化後も必要。
            LogGraphCallFailed(logger, status, requestId, returnedClientRequestId,
                GraphErrorFormatter.DiagnosticSummary(body));
        }

        response.Dispose();
        throw new GraphException(status, GraphErrorFormatter.Format(status, body, requestId, returnedClientRequestId),
            requestId, returnedClientRequestId);
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage clone = new(source.Method, source.RequestUri)
        {
            Version = source.Version, VersionPolicy = source.VersionPolicy
        };
        foreach (KeyValuePair<string, IEnumerable<string>> header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (source.Content is not null)
        {
            ByteArrayContent content = new(await source.Content.ReadAsByteArrayAsync(cancellationToken));
            foreach (KeyValuePair<string, IEnumerable<string>> header in source.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "ユーザーの直接参照で見つからなかったため検索へ切り替えます。StatusCode={StatusCode}, RequestId={RequestId}, ClientRequestId={ClientRequestId}")]
    private static partial void LogFallbackToSearch(ILogger logger, HttpStatusCode statusCode,
        string? requestId, string clientRequestId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "Graph API呼び出しに失敗しました。StatusCode={StatusCode}, RequestId={RequestId}, ClientRequestId={ClientRequestId}, Diagnostic={Diagnostic}")]
    private static partial void LogGraphCallFailed(ILogger logger, HttpStatusCode statusCode,
        string? requestId, string clientRequestId, string diagnostic);
}
