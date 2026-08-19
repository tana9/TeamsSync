using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;

using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>読み取り用と更新用の名前付きHTTPクライアントからGraph SDKクライアントを構成する</summary>
/// <param name="factory">名前付きHTTPクライアントを作成するファクトリ</param>
/// <param name="authentication">Graphのアクセストークンを取得する認証サービス</param>
/// <param name="logger">Graph通信の診断情報を記録するロガー</param>
/// <param name="identifierGenerator">リクエストIDの生成に使うID生成器。省略時は既定の実装を使う</param>
public sealed class GraphSdkClient(IHttpClientFactory factory, IAuthenticationService authentication,
    ILogger<GraphSdkLogCategory> logger, IIdentifierGenerator? identifierGenerator = null)
{
    private readonly GraphServiceClient _read =
        Create(factory.CreateClient(GraphEndpoints.ReadHttpClientName), authentication, logger, identifierGenerator);

    private readonly GraphServiceClient _write =
        Create(factory.CreateClient(GraphEndpoints.WriteHttpClientName), authentication, logger, identifierGenerator);

    // フィールド初期化子は他の非staticフィールドを参照できないため、認証プロバイダーは
    // 呼び出しごとに構成する(状態を持たない薄いラッパーのため、共有しなくても実害はない)
    private static GraphServiceClient Create(HttpClient transport, IAuthenticationService authentication,
        ILogger<GraphSdkLogCategory> logger, IIdentifierGenerator? identifierGenerator)
    {
        BaseBearerTokenAuthenticationProvider auth = new(new MsalAccessTokenProvider(authentication));
        HttpClient client = new(new GraphSdkTransportHandler(transport, logger, identifierGenerator))
        {
            BaseAddress = GraphEndpoints.BaseUri
        };
        return new GraphServiceClient(client, auth);
    }

    /// <summary>サインイン中のユーザー情報を取得する</summary>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>サインイン中のユーザーを表すGraph SDKモデル</returns>
    public async Task<User> GetMeAsync(CancellationToken cancellationToken)
    {
        return await _read.Me.GetAsync(config =>
                   config.QueryParameters.Select = ["id", "displayName", "userPrincipalName"], cancellationToken)
               ?? throw new InvalidDataException("Graphからユーザー情報が返されませんでした。");
    }

    /// <summary>サインイン中のユーザーが参加しているすべてのチームを取得する</summary>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>参加しているチームの一覧</returns>
    public async Task<IReadOnlyList<Team>> GetJoinedTeamsAsync(CancellationToken cancellationToken)
    {
        TeamCollectionResponse? response = await _read.Me.JoinedTeams.GetAsync(cancellationToken: cancellationToken);
        return await CollectAllPagesAsync<Team, TeamCollectionResponse>(response, cancellationToken);
    }

    /// <summary>サインイン中のユーザーが所有するすべてのディレクトリオブジェクト(グループ・アプリ等)を取得する</summary>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>所有するディレクトリオブジェクトの一覧</returns>
    public async Task<IReadOnlyList<DirectoryObject>> GetOwnedObjectsAsync(CancellationToken cancellationToken)
    {
        DirectoryObjectCollectionResponse? response =
            await _read.Me.OwnedObjects.GetAsync(cancellationToken: cancellationToken);
        return await CollectAllPagesAsync<DirectoryObject, DirectoryObjectCollectionResponse>(
            response, cancellationToken);
    }

    /// <summary>指定したチームのすべてのメンバーを取得する</summary>
    /// <param name="teamId">メンバーを取得するチームのオブジェクトID</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>チームに所属するメンバーの一覧</returns>
    public async Task<IReadOnlyList<ConversationMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken)
    {
        ConversationMemberCollectionResponse? response = await _read.Teams[teamId].Members.GetAsync(
            config => config.QueryParameters.Top = GraphEndpoints.MaxMembersPageSize,
            cancellationToken);
        return await CollectAllPagesAsync<ConversationMember, ConversationMemberCollectionResponse>(
            response, cancellationToken);
    }

    /// <summary>指定した識別子に一致するユーザーを取得する</summary>
    /// <param name="identifier">ユーザーのオブジェクトIDまたはユーザープリンシパル名</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>一致するユーザー。存在しない場合はnull</returns>
    public Task<User?> GetUserAsync(string identifier, CancellationToken cancellationToken)
    {
        return _read.Users[identifier].GetAsync(config =>
        {
            config.QueryParameters.Select = ["id", "displayName", "userPrincipalName", "mail"];
            config.Headers.Add("x-teams-sync-expected-not-found", "true");
        }, cancellationToken);
    }

    /// <summary>ODataフィルターまたは検索文字列を使ってディレクトリのユーザーを検索する</summary>
    /// <param name="filter">適用するODataフィルター。使用しない場合はnull</param>
    /// <param name="search">適用する検索文字列。使用しない場合はnull</param>
    /// <param name="top">取得する最大件数</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>検索条件に一致したユーザーの一覧</returns>
    public async Task<IReadOnlyList<User>> FindUsersAsync(string? filter, string? search, int top,
        CancellationToken cancellationToken)
    {
        UserCollectionResponse? response = await _read.Users.GetAsync(config =>
        {
            config.QueryParameters.Filter = filter;
            config.QueryParameters.Search = search;
            config.QueryParameters.Count = search is not null ? true : null;
            config.QueryParameters.Select = ["id", "displayName", "userPrincipalName", "mail"];
            config.QueryParameters.Top = top;
            if (search is not null)
            {
                config.Headers.Add("ConsistencyLevel", "eventual");
            }
        }, cancellationToken);
        return await CollectAllPagesAsync<User, UserCollectionResponse>(response, cancellationToken,
            search is null
                ? null
                : request =>
                {
                    request.Headers.Add("ConsistencyLevel", "eventual");
                    return request;
                });
    }

    /// <summary>
    ///     <c>@odata.nextLink</c>を辿って全ページを取得する共通処理。<see cref="GetJoinedTeamsAsync" />・
    ///     <see cref="GetTeamMembersAsync" />・<see cref="FindUsersAsync" />はいずれも
    ///     「レスポンスがnullなら空リスト→PageIteratorで全件収集」という同じ骨格のため、ここへ集約している
    /// </summary>
    private async Task<IReadOnlyList<TItem>> CollectAllPagesAsync<TItem, TResponse>(TResponse? response,
        CancellationToken cancellationToken,
        Func<RequestInformation, RequestInformation>? requestConfigurator = null)
        where TResponse : class, IParsable, IAdditionalDataHolder, new()
    {
        List<TItem> result = [];
        if (response is null)
        {
            return result;
        }

        PageIterator<TItem, TResponse> iterator = PageIterator<TItem, TResponse>.CreatePageIterator(
            _read, response, item =>
            {
                result.Add(item);
                return true;
            }, requestConfigurator);
        await iterator.IterateAsync(cancellationToken);
        return result;
    }

    /// <summary>指定したユーザーをチームの一般メンバーとして追加する</summary>
    /// <param name="teamId">追加先チームのオブジェクトID</param>
    /// <param name="userId">追加するユーザーのオブジェクトID</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    public Task AddMemberAsync(string teamId, string userId, CancellationToken cancellationToken)
    {
        AadUserConversationMember member = new()
        {
            Roles = [],
            AdditionalData = new Dictionary<string, object>
            {
                ["user@odata.bind"] = $"{GraphEndpoints.BaseUri}users('{userId}')"
            }
        };
        return _write.Teams[teamId].Members.PostAsync(member, cancellationToken: cancellationToken);
    }

    /// <summary>指定したメンバーシップをチームから削除する</summary>
    /// <param name="teamId">削除元チームのオブジェクトID</param>
    /// <param name="membershipId">削除するメンバーシップのオブジェクトID</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    public Task RemoveMemberAsync(string teamId, string membershipId, CancellationToken cancellationToken)
    {
        return _write.Teams[teamId].Members[membershipId].DeleteAsync(cancellationToken: cancellationToken);
    }
}