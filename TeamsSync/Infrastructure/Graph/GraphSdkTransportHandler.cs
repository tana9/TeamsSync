using Microsoft.Extensions.Logging;

using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>SDK要求を既存の名前付きHttpClientへ転送し、診断情報と例外形式を維持する</summary>
internal sealed class GraphSdkTransportHandler(HttpClient transport, ILogger<GraphHttpClient> logger,
    IIdentifierGenerator? identifierGenerator = null)
    : HttpMessageHandler
{
    private readonly IIdentifierGenerator _identifierGenerator = identifierGenerator ?? new IdentifierGenerator();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        GraphEndpointValidator.Validate(request.RequestUri);
        bool expectedNotFound = request.Headers.Remove("x-teams-sync-expected-not-found");
        string clientRequestId = GraphRequestDiagnostics.ApplyClientRequestId(request, _identifierGenerator);
        using HttpRequestMessage forwarded = await CloneAsync(request, cancellationToken);
        HttpResponseMessage response = await transport.SendAsync(forwarded, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        throw await GraphErrorHandler.HandleAsync(response, clientRequestId, expectedNotFound, logger,
            cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage clone = new(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
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
}