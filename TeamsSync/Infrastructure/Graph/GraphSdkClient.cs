using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

/// <summary>Microsoft Graph公式SDKを使った通常のGraph API操作を提供する。</summary>
public sealed class GraphSdkClient
{
    private readonly GraphServiceClient _read;
    private readonly GraphServiceClient _write;

    public GraphSdkClient(IHttpClientFactory factory, IAuthenticationService authentication,
        ILogger<GraphHttpClient> logger)
    {
        var auth = new BaseBearerTokenAuthenticationProvider(new MsalAccessTokenProvider(authentication));
        _read = Create(factory.CreateClient(GraphHttpClient.ReadHttpClientName), auth, logger);
        _write = Create(factory.CreateClient(GraphHttpClient.WriteHttpClientName), auth, logger);
    }

    private static GraphServiceClient Create(HttpClient transport,
        BaseBearerTokenAuthenticationProvider authentication, ILogger<GraphHttpClient> logger)
    {
        var client = new HttpClient(new GraphSdkTransportHandler(transport, logger))
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
        };
        return new GraphServiceClient(client, authentication);
    }

    public async Task<User> GetMeAsync(CancellationToken cancellationToken)
    {
        return await _read.Me.GetAsync(config =>
            config.QueryParameters.Select = ["id", "displayName", "userPrincipalName"], cancellationToken)
            ?? throw new InvalidDataException("Graphからユーザー情報が返されませんでした。");
    }

    public async Task<IReadOnlyList<Team>> GetJoinedTeamsAsync(CancellationToken cancellationToken)
    {
        var response = await _read.Me.JoinedTeams.GetAsync(cancellationToken: cancellationToken);
        List<Team> result = [];
        if (response is null) return result;
        var iterator = PageIterator<Team, TeamCollectionResponse>.CreatePageIterator(
            _read, response, item => { result.Add(item); return true; });
        await iterator.IterateAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<ConversationMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken)
    {
        var response = await _read.Teams[teamId].Members.GetAsync(config => config.QueryParameters.Top = 999,
            cancellationToken);
        List<ConversationMember> result = [];
        if (response is null) return result;
        var iterator = PageIterator<ConversationMember, ConversationMemberCollectionResponse>.CreatePageIterator(
            _read, response, item => { result.Add(item); return true; });
        await iterator.IterateAsync(cancellationToken);
        return result;
    }

    public Task<User?> GetUserAsync(string identifier, CancellationToken cancellationToken) =>
        _read.Users[identifier].GetAsync(config =>
        {
            config.QueryParameters.Select = ["id", "displayName", "userPrincipalName", "mail"];
            config.Headers.Add("x-teams-sync-expected-not-found", "true");
        }, cancellationToken);

    public async Task<IReadOnlyList<User>> FindUsersAsync(string? filter, string? search, int top,
        CancellationToken cancellationToken)
    {
        var response = await _read.Users.GetAsync(config =>
        {
            config.QueryParameters.Filter = filter;
            config.QueryParameters.Search = search;
            config.QueryParameters.Count = search is not null ? true : null;
            config.QueryParameters.Select = ["id", "displayName", "userPrincipalName", "mail"];
            config.QueryParameters.Top = top;
            if (search is not null) config.Headers.Add("ConsistencyLevel", "eventual");
        }, cancellationToken);
        List<User> result = [];
        if (response is null) return result;
        var iterator = PageIterator<User, UserCollectionResponse>.CreatePageIterator(
            _read, response, item => { result.Add(item); return true; },
            search is null ? null : request =>
            {
                request.Headers.Add("ConsistencyLevel", "eventual");
                return request;
            });
        await iterator.IterateAsync(cancellationToken);
        return result;
    }

    public Task AddMemberAsync(string teamId, string userId, CancellationToken cancellationToken)
    {
        var member = new AadUserConversationMember
        {
            Roles = [],
            AdditionalData = new Dictionary<string, object>
            {
                ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{userId}')"
            }
        };
        return _write.Teams[teamId].Members.PostAsync(member, cancellationToken: cancellationToken);
    }

    public Task RemoveMemberAsync(string teamId, string membershipId, CancellationToken cancellationToken) =>
        _write.Teams[teamId].Members[membershipId].DeleteAsync(cancellationToken: cancellationToken);
}
