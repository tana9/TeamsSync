using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
///     Microsoft Graphへの生のHTTP通信(認証ヘッダー付与、エラー変換、ページング、バッチ)を担う。
///     チーム・ユーザーに関するドメインロジックは<see cref="GraphTeamsGateway" />が持つ
/// </summary>
public sealed partial class GraphHttpClient(
    IHttpClientFactory httpClientFactory,
    IAuthenticationService auth,
    ILogger<GraphHttpClient> logger)
{
    public const string ReadHttpClientName = "MicrosoftGraph.Read";
    public const string WriteHttpClientName = "MicrosoftGraph.Write";
    private static readonly Uri GraphBase = new("https://graph.microsoft.com/v1.0/");

    /// <summary>
    ///     GETリクエストを送信し、レスポンスボディをJSONとして解析する
    /// </summary>
    public async Task<JsonDocument> GetAsync(string relative, bool expectedNotFound = false,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, relative);
        using HttpResponseMessage response = await SendOnceAsync(
            request, ReadHttpClientName, expectedNotFound, cancellationToken);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     <c>@odata.nextLink</c>を辿りながら全ページを取得し、<c>value</c>配列の要素をまとめて返す
    /// </summary>
    public async Task<List<JsonElement>> GetPagedAsync(string relative, CancellationToken cancellationToken)
    {
        List<JsonElement> result = [];
        string? next = relative;
        while (next is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using JsonDocument doc = await GetAsync(next, cancellationToken: cancellationToken);
            result.AddRange(doc.RootElement.GetProperty("value").EnumerateArray().Select(x => x.Clone()));
            next = doc.RootElement.TryGetProperty("@odata.nextLink", out JsonElement link)
                ? link.GetString()
                : null;
        }

        return result;
    }

    /// <summary>
    ///     更新系リクエストを送信する。<paramref name="replaceNames" />がtrueの場合、
    ///     C#では使えない<c>@odata.type</c>/<c>user@odata.bind</c>相当のプロパティ名へ置換する
    /// </summary>
    public async Task SendAsync(HttpMethod method, string relative, object? body = null, bool replaceNames = false,
        CancellationToken cancellationToken = default)
    {
        string? json = body is null ? null : JsonSerializer.Serialize(body);
        if (replaceNames && json is not null)
        {
            json = json.Replace("\"odata_type\"", "\"@odata.type\"")
                .Replace("\"user_odata_bind\"", "\"user@odata.bind\"");
        }

        using HttpRequestMessage request = CreateRequest(method, relative, json);
        using HttpResponseMessage response = await SendOnceAsync(
            request, WriteHttpClientName, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     複数のGETリクエストを<c>$batch</c>エンドポイントへまとめて送信し、リクエストIDごとの
    ///     レスポンスを返す
    /// </summary>
    public async Task<Dictionary<string, JsonElement>> SendBatchAsync(
        IReadOnlyList<(string Id, string Url)> requests, CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(new
        {
            requests = requests.Select(r => new { id = r.Id, method = "GET", url = r.Url })
        });
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "$batch", payload);
        using HttpResponseMessage response = await SendOnceAsync(
            request, ReadHttpClientName, cancellationToken: cancellationToken);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return doc.RootElement.GetProperty("responses").EnumerateArray()
            .ToDictionary(r => Required(r, "id"), r => r.Clone());
    }

    /// <summary>
    ///     相対/絶対URLからGraph向けの<see cref="HttpRequestMessage" />を組み立てる。
    ///     Graph以外のホストへのリクエストは拒否する
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string relative, string? json = null)
    {
        Uri uri = Uri.TryCreate(relative, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(GraphBase, relative);
        GraphEndpointValidator.Validate(uri);

        HttpRequestMessage request = new(method, uri);
        if (Uri.UnescapeDataString(uri.Query).Contains("$search=", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>
    ///     アクセストークンと診断用ヘッダーを付与してリクエストを1回送信し、失敗時は
    ///     <see cref="GraphException" />へ変換する
    /// </summary>
    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request,
        string clientName, bool expectedNotFound = false, CancellationToken cancellationToken = default)
    {
        string clientRequestId = Guid.NewGuid().ToString();
        request.Headers.TryAddWithoutValidation("client-request-id", clientRequestId);
        request.Headers.TryAddWithoutValidation("return-client-request-id", "true");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await auth.GetTokenAsync(cancellationToken: cancellationToken));
        HttpResponseMessage response = await httpClientFactory.CreateClient(clientName)
            .SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

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
        throw new GraphException(status, GraphErrorFormatter.Format(status, text, requestId, returnedClientRequestId),
            requestId, returnedClientRequestId);
    }

    /// <summary>レスポンスヘッダーの最初の値を取得する(存在しない場合はnull)</summary>
    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
    }

    /// <summary>JSON要素から文字列プロパティを取得する。存在しない場合は例外をスローする</summary>
    public static string Required(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"{name} がありません。");
    }

    /// <summary>JSON要素から文字列プロパティを取得する。存在しない場合はnullを返す</summary>
    public static string? Optional(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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