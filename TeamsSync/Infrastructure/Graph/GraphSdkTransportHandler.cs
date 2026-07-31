using Microsoft.Extensions.Logging;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>SDK要求を既存の名前付きHttpClientへ転送し、診断情報と例外形式を維持する。</summary>
internal sealed class GraphSdkTransportHandler(HttpClient transport, ILogger<GraphHttpClient> logger)
    : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clientRequestId = Guid.NewGuid().ToString();
        var expectedNotFound = request.Headers.Remove("x-teams-sync-expected-not-found");
        request.Headers.TryAddWithoutValidation("client-request-id", clientRequestId);
        request.Headers.TryAddWithoutValidation("return-client-request-id", "true");
        using var forwarded = await CloneAsync(request, cancellationToken);
        var response = await transport.SendAsync(forwarded, cancellationToken);
        if (response.IsSuccessStatusCode) return response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = response.StatusCode;
        var requestId = Header(response, "request-id");
        var returnedClientRequestId = Header(response, "client-request-id") ?? clientRequestId;
        if (expectedNotFound && status == System.Net.HttpStatusCode.NotFound)
            logger.LogDebug(
                "ユーザーの直接参照で見つからなかったため検索へ切り替えます。StatusCode={StatusCode}, RequestId={RequestId}, ClientRequestId={ClientRequestId}",
                status, requestId, returnedClientRequestId);
        else
            logger.LogError(
                "Graph API呼び出しに失敗しました。StatusCode={StatusCode}, RequestId={RequestId}, ClientRequestId={ClientRequestId}",
                status, requestId, returnedClientRequestId);
        response.Dispose();
        throw new GraphException(status, $"Graph API エラー ({(int)status}): {body}",
            requestId, returnedClientRequestId);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };
        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (source.Content is not null)
        {
            var content = new ByteArrayContent(await source.Content.ReadAsByteArrayAsync(cancellationToken));
            foreach (var header in source.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }
        return clone;
    }
}
