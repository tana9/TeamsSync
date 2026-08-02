using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Authentication;

using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>Microsoft Graph公式SDKを使った通常のGraph API操作を提供する</summary>
public sealed class GraphSdkClient
{
    private readonly GraphServiceClient _read;
    private readonly GraphServiceClient _write;

    /// <summary>読み取り用と更新用の名前付きHTTPクライアントからGraph SDKクライアントを構成する</summary>
    /// <param name="factory">名前付きHTTPクライアントを作成するファクトリ</param>
    /// <param name="authentication">Graphのアクセストークンを取得する認証サービス</param>
    /// <param name="logger">Graph通信の診断情報を記録するロガー</param>
    public GraphSdkClient(IHttpClientFactory factory, IAuthenticationService authentication,
        ILogger<GraphHttpClient> logger)
    {
        BaseBearerTokenAuthenticationProvider auth = new(new MsalAccessTokenProvider(authentication));
        _read = Create(factory.CreateClient(GraphHttpClient.ReadHttpClientName), auth, logger);
        _write = Create(factory.CreateClient(GraphHttpClient.WriteHttpClientName), auth, logger);
    }

    private static GraphServiceClient Create(HttpClient transport,
        BaseBearerTokenAuthenticationProvider authentication, ILogger<GraphHttpClient> logger)
    {
        HttpClient client = new(new GraphSdkTransportHandler(transport, logger))
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
        };
        return new GraphServiceClient(client, authentication);
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
        List<Team> result = [];
        if (response is null)
        {
            return result;
        }

        PageIterator<Team, TeamCollectionResponse> iterator =
            PageIterator<Team, TeamCollectionResponse>.CreatePageIterator(
                _read, response, item =>
                {
                    result.Add(item);
                    return true;
                });
        await iterator.IterateAsync(cancellationToken);
        return result;
    }

    /// <summary>指定したチームのすべてのメンバーを取得する</summary>
    /// <param name="teamId">メンバーを取得するチームのオブジェクトID</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>チームに所属するメンバーの一覧</returns>
    public async Task<IReadOnlyList<ConversationMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken)
    {
        ConversationMemberCollectionResponse? response = await _read.Teams[teamId].Members.GetAsync(
            config => config.QueryParameters.Top = 999,
            cancellationToken);
        List<ConversationMember> result = [];
        if (response is null)
        {
            return result;
        }

        PageIterator<ConversationMember, ConversationMemberCollectionResponse> iterator =
            PageIterator<ConversationMember, ConversationMemberCollectionResponse>.CreatePageIterator(
                _read, response, item =>
                {
                    result.Add(item);
                    return true;
                });
        await iterator.IterateAsync(cancellationToken);
        return result;
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
        List<User> result = [];
        if (response is null)
        {
            return result;
        }

        PageIterator<User, UserCollectionResponse> iterator =
            PageIterator<User, UserCollectionResponse>.CreatePageIterator(
                _read, response, item =>
                {
                    result.Add(item);
                    return true;
                },
                search is null
                    ? null
                    : request =>
                    {
                        request.Headers.Add("ConsistencyLevel", "eventual");
                        return request;
                    });
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
                ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{userId}')"
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
