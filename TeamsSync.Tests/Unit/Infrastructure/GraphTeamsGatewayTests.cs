using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Graph;

namespace TeamsSync.Tests.Unit.Infrastructure;

public sealed class GraphTeamsGatewayTests
{
    [Fact]
    public async Task GetMe_CancelsInFlightHttpRequest()
    {
        TaskCompletionSource requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DelegateHandler handler = new(async (_, cancellationToken) =>
        {
            requestStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out FakeAuthentication authentication);
        using CancellationTokenSource cancellation = new();

        Task<(string Id, string DisplayName, string UserPrincipalName)>
            request = gateway.GetMeAsync(cancellation.Token);
        await requestStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(cancellation.Token, authentication.LastCancellationToken);
    }

    [Fact]
    public async Task GetMe_Gateway単体ではRetryAfterを再試行しない()
    {
        TaskCompletionSource responseReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DelegateHandler handler = new((_, _) =>
        {
            HttpResponseMessage response = new((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            responseReturned.SetResult();
            return Task.FromResult(response);
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);
        using CancellationTokenSource cancellation = new();

        Task<(string Id, string DisplayName, string UserPrincipalName)>
            request = gateway.GetMeAsync(cancellation.Token);
        await responseReturned.Task.WaitAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<GraphException>(() => request);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetTeamMembers_FollowsNextLink()
    {
        int call = 0;
        DelegateHandler handler = new((_, _) => Task.FromResult(handlerResponse()));

        HttpResponseMessage handlerResponse()
        {
            return ++call == 1
                ? JsonResponse("""
                               {"value":[{"id":"membership-1","userId":"user-1","displayName":"One","email":"one@example.com","roles":[]}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/teams/team-1/members?$skiptoken=next"}
                               """)
                : JsonResponse("""
                               {"value":[{"id":"membership-2","userId":"user-2","displayName":"Two","email":"two@example.com","roles":["owner"]}]}
                               """);
        }

        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamMember> members =
            await gateway.GetTeamMembersAsync("team-1", TestContext.Current.CancellationToken);

        Assert.Equal(2, members.Count);
        Assert.True(members[1].IsOwner);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetOwnedTeams_ownedObjectsとjoinedTeamsの積集合を表示名昇順で返す()
    {
        DelegateHandler handler = new((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("""
                                    {"value":[
                                      {"id":"group-2","displayName":"Bravo"},
                                      {"id":"group-4","displayName":"Alpha"},
                                      {"id":"group-9","displayName":"NotOwned"}
                                    ]}
                                    """));
            }

            Assert.EndsWith("/me/ownedObjects", path, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(JsonResponse("""
                                {"value":[
                                  {"@odata.type":"#microsoft.graph.group","id":"group-2","resourceProvisioningOptions":["Team"]},
                                  {"@odata.type":"#microsoft.graph.group","id":"group-4","resourceProvisioningOptions":["Team"]}
                                ]}
                                """));
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamInfo> owned =
            await gateway.GetOwnedTeamsAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "Bravo"], owned.Select(team => team.DisplayName));
    }

    [Fact]
    public async Task GetOwnedTeams_Team化されていないグループやグループ以外の所有オブジェクトは除外する()
    {
        DelegateHandler handler = new((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("""
                                    {"value":[
                                      {"id":"group-5","displayName":"PlainGroup"},
                                      {"id":"app-1","displayName":"NotAGroup"}
                                    ]}
                                    """));
            }

            Assert.EndsWith("/me/ownedObjects", path, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(JsonResponse("""
                                {"value":[
                                  {"@odata.type":"#microsoft.graph.group","id":"group-5","resourceProvisioningOptions":[]},
                                  {"@odata.type":"#microsoft.graph.application","id":"app-1"}
                                ]}
                                """));
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamInfo> owned =
            await gateway.GetOwnedTeamsAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Empty(owned);
    }

    [Fact]
    public async Task GetOwnedTeams_ownedObjectsのページングを辿る()
    {
        int call = 0;
        DelegateHandler handler = new((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("""
                                    {"value":[
                                      {"id":"group-1","displayName":"Alpha"},
                                      {"id":"group-2","displayName":"Bravo"}
                                    ]}
                                    """));
            }

            Assert.EndsWith("/me/ownedObjects", path.Split('?')[0], StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(++call == 1
                ? JsonResponse("""
                               {"value":[{"@odata.type":"#microsoft.graph.group","id":"group-1","resourceProvisioningOptions":["Team"]}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/ownedObjects?$skiptoken=next"}
                               """)
                : JsonResponse("""
                               {"value":[{"@odata.type":"#microsoft.graph.group","id":"group-2","resourceProvisioningOptions":["Team"]}]}
                               """));
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamInfo> owned =
            await gateway.GetOwnedTeamsAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "Bravo"], owned.Select(team => team.DisplayName));
        Assert.Equal(2, call);
    }

    [Fact]
    public async Task GetMe_Gateway単体は429を基盤へ委譲し相関ヘッダーを付ける()
    {
        string? clientRequestId = null;
        int handlerCallCount = 0;
        DelegateHandler handler = new((request, _) =>
        {
            clientRequestId = request.Headers.GetValues("client-request-id").Single();
            Assert.Equal("true", request.Headers.GetValues("return-client-request-id").Single());
            if (handlerCallCount++ == 0)
            {
                HttpResponseMessage throttled = new((HttpStatusCode)429);
                throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(throttled);
            }

            return Task.FromResult(JsonResponse(
                "{\"id\":\"me\",\"displayName\":\"Me\",\"userPrincipalName\":\"me@example.com\"}"));
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        await Assert.ThrowsAsync<GraphException>(() =>
            gateway.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.CallCount);
        Assert.False(string.IsNullOrWhiteSpace(clientRequestId));
    }

    [Fact]
    public async Task GetMe_Gateway単体は503を一度でGraph例外へ変換する()
    {
        DelegateHandler handler = new((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            response.Content = new StringContent("service unavailable");
            return Task.FromResult(response);
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        GraphException exception = await Assert.ThrowsAsync<GraphException>(() =>
            gateway.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetMe_IncludesSafeErrorSummaryAndCorrelationIdsInGraphException()
    {
        string? sentClientRequestId = null;
        DelegateHandler handler = new((request, _) =>
        {
            sentClientRequestId = request.Headers.GetValues("client-request-id").Single();
            HttpResponseMessage response = new(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":\"Authorization_RequestDenied\",\"message\":\"Access denied\",\"innerError\":{\"secret\":\"do-not-show\"}}}")
            };
            response.Headers.TryAddWithoutValidation("request-id", "graph-request-id");
            response.Headers.TryAddWithoutValidation("client-request-id", sentClientRequestId);
            return Task.FromResult(response);
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        GraphException exception = await Assert.ThrowsAsync<GraphException>(() =>
            gateway.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Authorization_RequestDenied", exception.Message);
        Assert.Contains("Access denied", exception.Message);
        Assert.DoesNotContain("do-not-show", exception.Message);
        Assert.Contains("graph-request-id", exception.Message);
        Assert.Contains(sentClientRequestId!, exception.Message);
        Assert.Equal("graph-request-id", exception.RequestId);
        Assert.Equal(sentClientRequestId, exception.ClientRequestId);
    }

    [Fact]
    public async Task GetMe_PropagatesNetworkFailureWithoutRetrying()
    {
        DelegateHandler handler = new((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("network failure")));
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            gateway.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetMe_PropagatesTimeoutWithoutRetrying()
    {
        DelegateHandler handler = new((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("request timeout")));
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            gateway.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task MemberWrite_ReportsDeletedOrDuplicateMemberError(HttpStatusCode statusCode)
    {
        DelegateHandler handler = new((_, _) => Task.FromResult(
            new HttpResponseMessage(statusCode) { Content = new StringContent("member write failed") }));
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        GraphException exception = statusCode == HttpStatusCode.NotFound
            ? await Assert.ThrowsAsync<GraphException>(() => gateway.RemoveMemberAsync(
                "team-1", "deleted-membership", TestContext.Current.CancellationToken))
            : await Assert.ThrowsAsync<GraphException>(() => gateway.AddMemberAsync(
                "team-1", "11111111-1111-1111-1111-111111111111", TestContext.Current.CancellationToken));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.DoesNotContain("member write failed", exception.Message);
        Assert.Contains(((int)statusCode).ToString(), exception.Message);
    }

    [Fact]
    public async Task FindUsers_SearchesDisplayNameWithConsistencyHeader()
    {
        int searchHeaderCount = 0;
        DelegateHandler handler = new((request, _) =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            if (query.Contains("$search=", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Headers.TryGetValues("ConsistencyLevel", out IEnumerable<string>? values) &&
                    values.Single() == "eventual")
                {
                    searchHeaderCount++;
                }

                return Task.FromResult(JsonResponse("""
                                                    {"value":[{"id":"user-1","displayName":"山田 太郎","userPrincipalName":"yamada@example.com","mail":"yamada@example.com"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=next"}
                                                    """));
            }

            if (query.Contains("$skiptoken=", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Headers.TryGetValues("ConsistencyLevel", out IEnumerable<string>? values) &&
                    values.Single() == "eventual")
                {
                    searchHeaderCount++;
                }

                return Task.FromResult(JsonResponse("""
                                                    {"value":[{"id":"user-2","displayName":"山田 太郎","userPrincipalName":"yamada2@example.com","mail":"yamada2@example.com"}]}
                                                    """));
            }

            if (query.Contains("$filter=", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("{\"value\":[]}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"message\":\"not found\"}}")
            });
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<DirectoryUser>
            users = await gateway.FindUsersAsync("山田太郎", TestContext.Current.CancellationToken);

        Assert.Equal(2, users.Count);
        Assert.Equal("user-1", users[0].Id);
        Assert.Equal(2, searchHeaderCount);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task FindUsers_直接参照404から検索成功した場合はErrorログを残さない()
    {
        DelegateHandler handler = new((request, _) =>
        {
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            return Task.FromResult(query.Contains("$filter=", StringComparison.OrdinalIgnoreCase)
                ? JsonResponse("""
                               {"value":[{"id":"user-1","displayName":"User","userPrincipalName":"user@example.com","mail":"user@example.com"}]}
                               """)
                : new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":{\"message\":\"not found\"}}")
                });
        });
        RecordingLogger<GraphSdkLogCategory> logger = new();
        GraphTeamsGateway gateway = CreateGateway(handler, out _, logger);

        IReadOnlyList<DirectoryUser> users =
            await gateway.FindUsersAsync("user@example.com", TestContext.Current.CancellationToken);

        Assert.Single(users);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Debug &&
                                                 entry.Message.Contains("検索へ切り替え"));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task GetMe_通常の404はErrorログへ記録する()
    {
        DelegateHandler handler = new((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") }));
        RecordingLogger<GraphSdkLogCategory> logger = new();
        GraphTeamsGateway gateway = CreateGateway(handler, out _, logger);

        await Assert.ThrowsAsync<GraphException>(() =>
            gateway.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error &&
                                                 entry.Message.Contains("Graph API呼び出しに失敗"));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static GraphTeamsGateway CreateGateway(HttpMessageHandler handler, out FakeAuthentication authentication,
        ILogger<GraphSdkLogCategory>? httpLogger = null, ILogger<GraphTeamsGateway>? gatewayLogger = null)
    {
        authentication = new FakeAuthentication();
        HttpClient client = new(handler);
        GraphSdkClient sdk = new(new FakeHttpClientFactory(client), authentication,
            httpLogger ?? NullLogger<GraphSdkLogCategory>.Instance);
        ILogger<GraphTeamsGateway> logger = gatewayLogger ?? NullLogger<GraphTeamsGateway>.Instance;
        return new GraphTeamsGateway(sdk, logger, new GraphUserSearchService(sdk));
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return client;
        }
    }

    private sealed class FakeAuthentication : IAuthenticationService
    {
        public CancellationToken LastCancellationToken { get; private set; }
        public string? UserName => "test@example.com";
        public string? TenantId => "tenant-id";

        public Task<string> GetTokenAsync(bool interactive = false, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult("token");
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return send(request, cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
