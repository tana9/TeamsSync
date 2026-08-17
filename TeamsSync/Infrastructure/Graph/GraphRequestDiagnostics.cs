using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

// GraphSdkTransportHandlerがGraphの診断用ヘッダー(client-request-id/return-client-request-id)を
// 付与する処理を独立したヘルパーへ切り出している
/// <summary>Graphへのリクエストに診断用ヘッダー(client-request-id)を付与する</summary>
internal static class GraphRequestDiagnostics
{
    /// <summary>
    ///     診断用ヘッダーを付与し、発行したclient-request-idを返す。
    ///     エラー発生時に<see cref="GraphErrorHandler" />・<see cref="GraphException" />へ引き継ぐために使う
    /// </summary>
    public static string ApplyClientRequestId(HttpRequestMessage request, IIdentifierGenerator identifierGenerator)
    {
        string clientRequestId = identifierGenerator.NewGuid().ToString();
        request.Headers.TryAddWithoutValidation("client-request-id", clientRequestId);
        request.Headers.TryAddWithoutValidation("return-client-request-id", "true");
        return clientRequestId;
    }
}