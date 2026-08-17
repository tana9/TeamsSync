using System.Net;

using Microsoft.Extensions.Logging;

namespace TeamsSync.Infrastructure.Graph;

// GraphSdkTransportHandlerがGraphからの失敗応答を診断ログ・GraphExceptionへの変換という
// 共通規約で扱えるよう、この処理を独立したヘルパーへ切り出している
/// <summary>Graph API呼び出しの失敗応答を、診断ログの記録と<see cref="GraphException" />への変換で共通に扱う</summary>
internal static partial class GraphErrorHandler
{
    /// <summary>
    ///     失敗レスポンスを診断ログへ記録し、<see cref="GraphException" />へ変換して返す。
    ///     レスポンスは呼び出し元へ返さないため、ここで<see cref="HttpResponseMessage.Dispose()" />する
    /// </summary>
    public static async Task<GraphException> HandleAsync(HttpResponseMessage response, string clientRequestId,
        bool expectedNotFound, ILogger logger, CancellationToken cancellationToken)
    {
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        HttpStatusCode status = response.StatusCode;
        string? requestId = Header(response, "request-id");
        string returnedClientRequestId = Header(response, "client-request-id") ?? clientRequestId;
        if (expectedNotFound && status == HttpStatusCode.NotFound)
        {
            LogFallbackToSearch(logger, status, requestId, returnedClientRequestId);
        }
        else if (logger.IsEnabled(LogLevel.Error))
        {
            // DiagnosticSummary(text)はJSON解析を伴うため、Errorログが無効な場合は評価しない(CA1873)。
            // [LoggerMessage]は内部でIsEnabledを判定するが、呼び出し側の引数(DiagnosticSummaryの呼び出し)
            // 自体はC#の評価順序上どのみ実行されてしまうため、このガードは生成メソッド化後も必要
            LogGraphCallFailed(logger, status, requestId, returnedClientRequestId,
                GraphErrorFormatter.DiagnosticSummary(text));
        }

        response.Dispose();
        return new GraphException(status, GraphErrorFormatter.Format(status, text, requestId, returnedClientRequestId),
            requestId, returnedClientRequestId);
    }

    /// <summary>レスポンスヘッダーの最初の値を取得する(存在しない場合はnull)</summary>
    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message =
            "ユーザーの直接参照で見つからなかったため検索へ切り替えます。StatusCode={StatusCode}, RequestId={RequestId}, ClientRequestId={ClientRequestId}")]
    private static partial void LogFallbackToSearch(ILogger logger, HttpStatusCode statusCode,
        string? requestId, string clientRequestId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message =
            "Graph API呼び出しに失敗しました。StatusCode={StatusCode}, RequestId={RequestId}, ClientRequestId={ClientRequestId}, Diagnostic={Diagnostic}")]
    private static partial void LogGraphCallFailed(ILogger logger, HttpStatusCode statusCode,
        string? requestId, string clientRequestId, string diagnostic);
}