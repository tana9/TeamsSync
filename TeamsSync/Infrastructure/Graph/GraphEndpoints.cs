namespace TeamsSync.Infrastructure.Graph;

// ホスト名・ベースURIが複数ファイル(HttpClient登録、SDKクライアント、応答URLの許可ホスト検証、
// トークン送信先の許可ホスト検証)に個別ハードコードされ、片方だけ更新するとドリフトする恐れが
// あったため1箇所へ集約する
/// <summary>Microsoft Graph APIのエンドポイント定数</summary>
internal static class GraphEndpoints
{
    /// <summary>Graph APIのホスト名</summary>
    public const string Host = "graph.microsoft.com";

    /// <summary>Graph API v1.0のベースURI</summary>
    public static readonly Uri BaseUri = new($"https://{Host}/v1.0/");

    /// <summary>Graph APIの読み取り要求に使用する名前付きHTTPクライアントの名前</summary>
    public const string ReadHttpClientName = "MicrosoftGraph.Read";

    /// <summary>Graph APIの更新要求に使用する名前付きHTTPクライアントの名前</summary>
    public const string WriteHttpClientName = "MicrosoftGraph.Write";

    /// <summary>チームメンバー一覧取得で指定する最大ページサイズ($top)。Graphが許容する上限値</summary>
    public const int MaxMembersPageSize = 999;
}