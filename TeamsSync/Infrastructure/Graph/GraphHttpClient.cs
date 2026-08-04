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
public sealed class GraphHttpClient(
    IHttpClientFactory httpClientFactory,
    IAuthenticationService auth,
    ILogger<GraphHttpClient> logger,
    IIdentifierGenerator? identifierGenerator = null)
{
    /// <summary>Graph APIの読み取り要求に使用する名前付きHTTPクライアントの名前</summary>
    public const string ReadHttpClientName = "MicrosoftGraph.Read";

    /// <summary>Graph APIの更新要求に使用する名前付きHTTPクライアントの名前</summary>
    public const string WriteHttpClientName = "MicrosoftGraph.Write";

    /// <summary>チームメンバー一覧取得で指定する最大ページサイズ($top)。Graphが許容する上限値</summary>
    public const int MaxMembersPageSize = 999;

    private readonly IIdentifierGenerator _identifierGenerator = identifierGenerator ?? new IdentifierGenerator();

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
            .ToDictionary(r => GraphResponseParser.Required(r, "id"), r => r.Clone());
    }

    /// <summary>
    ///     相対/絶対URLからGraph向けの<see cref="HttpRequestMessage" />を組み立てる。
    ///     Graph以外のホストへのリクエストは拒否する
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string relative, string? json = null)
    {
        Uri uri = Uri.TryCreate(relative, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(GraphEndpoints.BaseUri, relative);
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
        string clientRequestId = _identifierGenerator.NewGuid().ToString();
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

        throw await GraphErrorHandler.HandleAsync(response, clientRequestId, expectedNotFound, logger,
            cancellationToken);
    }
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