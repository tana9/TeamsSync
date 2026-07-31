using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>
/// Microsoft Graphへの生のHTTP通信(認証ヘッダー付与、エラー変換、ページング、バッチ)を担う。
/// チーム・ユーザーに関するドメインロジックは<see cref="GraphTeamsGateway"/>が持つ。
/// </summary>
public sealed class GraphHttpClient(
    IHttpClientFactory httpClientFactory,
    IAuthenticationService auth,
    ILogger<GraphHttpClient> logger)
{
    public const string ReadHttpClientName = "MicrosoftGraph.Read";
    public const string WriteHttpClientName = "MicrosoftGraph.Write";
    private static readonly Uri GraphBase = new("https://graph.microsoft.com/v1.0/");

    /// <summary>
    /// GETリクエストを送信し、レスポンスボディをJSONとして解析する。
    /// </summary>
    public async Task<JsonDocument> GetAsync(string relative, CancellationToken cancellationToken,
        bool expectedNotFound = false)
    {
        using var response = await SendOnceAsync(
            CreateRequest(HttpMethod.Get, relative), ReadHttpClientName, cancellationToken, expectedNotFound);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// <c>@odata.nextLink</c>を辿りながら全ページを取得し、<c>value</c>配列の要素をまとめて返す。
    /// </summary>
    public async Task<List<JsonElement>> GetPagedAsync(string relative, CancellationToken cancellationToken)
    {
        var result = new List<JsonElement>();
        var next = relative;
        while (next is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var doc = await GetAsync(next, cancellationToken);
            result.AddRange(doc.RootElement.GetProperty("value").EnumerateArray().Select(x => x.Clone()));
            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var link)
                ? link.GetString()
                : null;
        }

        return result;
    }

    /// <summary>
    /// 更新系リクエストを送信する。<paramref name="replaceNames"/>がtrueの場合、
    /// C#では使えない<c>@odata.type</c>/<c>user@odata.bind</c>相当のプロパティ名へ置換する。
    /// </summary>
    public async Task SendAsync(HttpMethod method, string relative, object? body = null, bool replaceNames = false,
        CancellationToken cancellationToken = default)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body);
        if (replaceNames && json is not null)
            json = json.Replace("\"odata_type\"", "\"@odata.type\"")
                .Replace("\"user_odata_bind\"", "\"user@odata.bind\"");
        using var response = await SendOnceAsync(
            CreateRequest(method, relative, json), WriteHttpClientName, cancellationToken);
    }

    /// <summary>
    /// 複数のGETリクエストを<c>$batch</c>エンドポイントへまとめて送信し、リクエストIDごとの
    /// レスポンスを返す。
    /// </summary>
    public async Task<Dictionary<string, JsonElement>> SendBatchAsync(
        IReadOnlyList<(string Id, string Url)> requests, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            requests = requests.Select(r => new { id = r.Id, method = "GET", url = r.Url })
        });
        using var response = await SendOnceAsync(
            CreateRequest(HttpMethod.Post, "$batch", payload), ReadHttpClientName, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return doc.RootElement.GetProperty("responses").EnumerateArray()
            .ToDictionary(r => Required(r, "id"), r => r.Clone());
    }

    /// <summary>
    /// 相対/絶対URLからGraph向けの<see cref="HttpRequestMessage"/>を組み立てる。
    /// Graph以外のホストへのリクエストは拒否する。
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string relative, string? json = null)
    {
        var uri = Uri.TryCreate(relative, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(GraphBase, relative);
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, GraphBase.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Graph APIの応答に許可されていないURLが含まれています。");
        var request = new HttpRequestMessage(method, uri);
        if (Uri.UnescapeDataString(uri.Query).Contains("$search=", StringComparison.OrdinalIgnoreCase))
            request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// アクセストークンと診断用ヘッダーを付与してリクエストを1回送信し、失敗時は
    /// <see cref="GraphException"/>へ変換する。
    /// </summary>
    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request,
        string clientName, CancellationToken cancellationToken, bool expectedNotFound = false)
    {
        var clientRequestId = Guid.NewGuid().ToString();
        request.Headers.TryAddWithoutValidation("client-request-id", clientRequestId);
        request.Headers.TryAddWithoutValidation("return-client-request-id", "true");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await auth.GetTokenAsync(cancellationToken: cancellationToken));
        var response = await httpClientFactory.CreateClient(clientName)
            .SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return response;
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = response.StatusCode;
        var requestId = Header(response, "request-id");
        var returnedClientRequestId = Header(response, "client-request-id") ?? clientRequestId;
        if (expectedNotFound && status == HttpStatusCode.NotFound)
            logger.LogDebug(
                "ユーザーの直接参照で見つからなかったため検索へ切り替えます。StatusCode={StatusCode}, RequestId={RequestId}, ClientRequestId={ClientRequestId}",
                status, requestId, returnedClientRequestId);
        else
            logger.LogError(
                "Graph API呼び出しに失敗しました。StatusCode={StatusCode}, RequestId={RequestId}, ClientRequestId={ClientRequestId}",
                status, requestId, returnedClientRequestId);
        response.Dispose();
        throw new GraphException(status, $"Graph API エラー ({(int)status}): {text}",
            requestId, returnedClientRequestId);
    }

    /// <summary>レスポンスヘッダーの最初の値を取得する(存在しない場合はnull)。</summary>
    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    /// <summary>JSON要素から文字列プロパティを取得する。存在しない場合は例外をスローする。</summary>
    public static string Required(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"{name} がありません。");
    }

    /// <summary>JSON要素から文字列プロパティを取得する。存在しない場合はnullを返す。</summary>
    public static string? Optional(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

/// <summary>Microsoft Graph API呼び出しが失敗したことを表す例外。</summary>
public sealed class GraphException(
    HttpStatusCode statusCode,
    string message,
    string? requestId = null,
    string? clientRequestId = null) : Exception(message)
{
    /// <summary>レスポンスのHTTPステータスコード。</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Graph側の診断用リクエストID(取得できた場合)。</summary>
    public string? RequestId { get; } = requestId;

    /// <summary>クライアント側で発行した診断用リクエストID。</summary>
    public string? ClientRequestId { get; } = clientRequestId;
}
