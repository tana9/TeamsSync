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
    public async Task GetOwnedTeams_バッチリクエストで所有判定しセッション内で再利用する()
    {
        int batchCalls = 0;
        DelegateHandler handler = new(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                                    {"value":[
                                      {"id":"team-1","displayName":"Zulu"},
                                      {"id":"team-2","displayName":"Alpha"},
                                      {"id":"team-3","displayName":"Charlie"},
                                      {"id":"team-4","displayName":"Bravo"},
                                      {"id":"team-5","displayName":"Echo"},
                                      {"id":"team-6","displayName":"Delta"}
                                    ]}
                                    """);
            }

            Assert.EndsWith("/$batch", path, StringComparison.OrdinalIgnoreCase);
            Interlocked.Increment(ref batchCalls);
            string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(requestBody);
            var responses = doc.RootElement.GetProperty("requests").EnumerateArray().Select(req =>
            {
                string? id = req.GetProperty("id").GetString();
                string url = req.GetProperty("url").GetString()!;
                string teamId = url.Split('/')[2];
                bool isOwned = teamId is "team-2" or "team-4";
                string memberBody = isOwned
                    ? """{"value":[{"id":"membership","userId":"current-user","displayName":"Me","email":"me@example.com","roles":["owner"]}]}"""
                    : """{"value":[]}""";
                return new { id, status = 200, body = JsonDocument.Parse(memberBody).RootElement };
            }).ToList();
            return JsonResponse(JsonSerializer.Serialize(new { responses }));
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamInfo> first = await gateway.GetOwnedTeamsAsync(
            "current-user", TestContext.Current.CancellationToken);
        IReadOnlyList<TeamInfo> second = await gateway.GetOwnedTeamsAsync(
            "current-user", TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "Bravo"], first.Select(team => team.DisplayName));
        Assert.Equal(first, second);
        Assert.Equal(1, batchCalls);

        gateway.ClearOwnedTeamsCache("current-user");
        await gateway.GetOwnedTeamsAsync(
            "current-user", TestContext.Current.CancellationToken);

        Assert.Equal(2, batchCalls);
    }

    [Fact]
    public async Task GetOwnedTeams_20件を超える候補は複数バッチへ分割する()
    {
        const int teamCount = 25;
        List<int> batchSizes = new();
        DelegateHandler handler = new(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                string teams = string.Join(',', Enumerable.Range(1, teamCount)
                    .Select(i => $$"""{"id":"team-{{i}}","displayName":"Team {{i}}"}"""));
                return JsonResponse($$"""{"value":[{{teams}}]}""");
            }

            string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(requestBody);
            List<JsonElement> requestList = doc.RootElement.GetProperty("requests").EnumerateArray().ToList();
            lock (batchSizes)
            {
                batchSizes.Add(requestList.Count);
            }

            var responses = requestList.Select(req => new
            {
                id = req.GetProperty("id").GetString(),
                status = 200,
                body = JsonDocument.Parse("""{"value":[]}""").RootElement
            }).ToList();
            return JsonResponse(JsonSerializer.Serialize(new { responses }));
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        await gateway.GetOwnedTeamsAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Equal(2, batchSizes.Count);
        Assert.Equal(teamCount, batchSizes.Sum());
        Assert.Contains(20, batchSizes);
    }

    [Fact]
    public async Task GetOwnedTeams_バッチ内の403は待機せず個別取得へフォールバックする()
    {
        int individualCallCount = 0;
        DelegateHandler handler = new(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                                    {"value":[
                                      {"id":"team-1","displayName":"Alpha"},
                                      {"id":"team-2","displayName":"Bravo"}
                                    ]}
                                    """);
            }

            if (path.EndsWith("/$batch", StringComparison.OrdinalIgnoreCase))
            {
                string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using JsonDocument doc = JsonDocument.Parse(requestBody);
                // 403は権限不足であり待っても解決しないため、429/503と異なり即座に個別フォールバックへ回る。
                var responses = doc.RootElement.GetProperty("requests").EnumerateArray().Select(req =>
                {
                    string? id = req.GetProperty("id").GetString();
                    string url = req.GetProperty("url").GetString()!;
                    string teamId = url.Split('/')[2];
                    return teamId == "team-1"
                        ? new { id, status = 403, body = JsonDocument.Parse("{}").RootElement }
                        : new
                        {
                            id,
                            status = 200,
                            body = JsonDocument.Parse(
                                    """{"value":[{"id":"membership","userId":"current-user","displayName":"Me","email":"me@example.com","roles":["owner"]}]}""")
                                .RootElement
                        };
                }).ToList();
                return JsonResponse(JsonSerializer.Serialize(new { responses }));
            }

            Interlocked.Increment(ref individualCallCount);
            return JsonResponse(
                """{"value":[{"id":"membership","userId":"current-user","displayName":"Me","email":"me@example.com","roles":["owner"]}]}""");
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamInfo> owned =
            await gateway.GetOwnedTeamsAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "Bravo"], owned.Select(team => team.DisplayName));
        Assert.Equal(1, individualCallCount);
    }

    [Fact]
    public async Task GetOwnedTeams_バッチ内の429はRetryAfter待機後に同じ項目だけ再試行し個別取得へ回さない()
    {
        int batchCallCount = 0;
        List<int> requestCountsPerCall = new();
        int individualCallCount = 0;
        DelegateHandler handler = new(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                                    {"value":[
                                      {"id":"team-1","displayName":"Alpha"},
                                      {"id":"team-2","displayName":"Bravo"}
                                    ]}
                                    """);
            }

            if (path.EndsWith("/$batch", StringComparison.OrdinalIgnoreCase))
            {
                int callIndex = Interlocked.Increment(ref batchCallCount);
                string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using JsonDocument doc = JsonDocument.Parse(requestBody);
                List<JsonElement> requestList = doc.RootElement.GetProperty("requests").EnumerateArray().ToList();
                lock (requestCountsPerCall)
                {
                    requestCountsPerCall.Add(requestList.Count);
                }

                // team-1は1回目のみ429(Retry-After=0秒)を返し、2回目以降は200を返す。
                // team-2は最初から200(所有者ではない)。
                var responses = requestList.Select(req =>
                {
                    string? id = req.GetProperty("id").GetString();
                    string url = req.GetProperty("url").GetString()!;
                    string teamId = url.Split('/')[2];
                    if (teamId == "team-1" && callIndex == 1)
                    {
                        return new
                        {
                            id,
                            status = 429,
                            headers = new Dictionary<string, string> { ["Retry-After"] = "0" },
                            body = JsonDocument.Parse("{}").RootElement
                        };
                    }

                    return new
                    {
                        id,
                        status = 200,
                        headers = new Dictionary<string, string>(),
                        body = JsonDocument.Parse(teamId == "team-1"
                                ? """{"value":[{"id":"membership","userId":"current-user","displayName":"Me","email":"me@example.com","roles":["owner"]}]}"""
                                : """{"value":[]}""")
                            .RootElement
                    };
                }).ToList();
                return JsonResponse(JsonSerializer.Serialize(new { responses }));
            }

            Interlocked.Increment(ref individualCallCount);
            return JsonResponse("""{"value":[]}""");
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamInfo> owned =
            await gateway.GetOwnedTeamsAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha"], owned.Select(team => team.DisplayName));
        Assert.Equal(0, individualCallCount);
        Assert.Equal(2, batchCallCount);
        // 1回目は2件(team-1, team-2)、2回目の再試行は429だったteam-1だけを含む1件のみ。
        Assert.Equal([2, 1], requestCountsPerCall);
    }

    [Fact]
    public async Task GetOwnedTeams_バッチ内の503も429と同様に待機後に再試行する()
    {
        int batchCallCount = 0;
        int individualCallCount = 0;
        DelegateHandler handler = new(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"value":[{"id":"team-1","displayName":"Alpha"}]}""");
            }

            int callIndex = Interlocked.Increment(ref batchCallCount);
            string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(requestBody);
            var responses = doc.RootElement.GetProperty("requests").EnumerateArray().Select(req =>
            {
                string? id = req.GetProperty("id").GetString();
                return callIndex == 1
                    ? new
                    {
                        id,
                        status = 503,
                        headers = new Dictionary<string, string> { ["Retry-After"] = "0" },
                        body = JsonDocument.Parse("{}").RootElement
                    }
                    : new
                    {
                        id,
                        status = 200,
                        headers = new Dictionary<string, string>(),
                        body = JsonDocument.Parse(
                                """{"value":[{"id":"membership","userId":"current-user","displayName":"Me","email":"me@example.com","roles":["owner"]}]}""")
                            .RootElement
                    };
            }).ToList();
            return JsonResponse(JsonSerializer.Serialize(new { responses }));
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamInfo> owned =
            await gateway.GetOwnedTeamsAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha"], owned.Select(team => team.DisplayName));
        Assert.Equal(0, individualCallCount);
        Assert.Equal(2, batchCallCount);
    }

    [Fact]
    public async Task GetOwnedTeams_429が再試行上限に達すると個別取得へフォールバックする()
    {
        int batchCallCount = 0;
        int individualCallCount = 0;
        DelegateHandler handler = new(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                                    {"value":[
                                      {"id":"team-1","displayName":"Alpha"},
                                      {"id":"team-2","displayName":"Bravo"}
                                    ]}
                                    """);
            }

            if (path.EndsWith("/$batch", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref batchCallCount);
                string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using JsonDocument doc = JsonDocument.Parse(requestBody);
                List<JsonElement> requestList = doc.RootElement.GetProperty("requests").EnumerateArray().ToList();
                // team-1は常に429を返し続け、再試行上限(3回)に達しても解消しないケースを再現する。
                var responses = requestList.Select(req =>
                {
                    string? id = req.GetProperty("id").GetString();
                    string url = req.GetProperty("url").GetString()!;
                    string teamId = url.Split('/')[2];
                    return teamId == "team-1"
                        ? new
                        {
                            id,
                            status = 429,
                            headers = new Dictionary<string, string> { ["Retry-After"] = "0" },
                            body = JsonDocument.Parse("{}").RootElement
                        }
                        : new
                        {
                            id,
                            status = 200,
                            headers = new Dictionary<string, string>(),
                            body = JsonDocument.Parse(
                                    """{"value":[{"id":"membership","userId":"current-user","displayName":"Me","email":"me@example.com","roles":["owner"]}]}""")
                                .RootElement
                        };
                }).ToList();
                return JsonResponse(JsonSerializer.Serialize(new { responses }));
            }

            Interlocked.Increment(ref individualCallCount);
            return JsonResponse(
                """{"value":[{"id":"membership","userId":"current-user","displayName":"Me","email":"me@example.com","roles":["owner"]}]}""");
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        IReadOnlyList<TeamInfo> owned =
            await gateway.GetOwnedTeamsAsync("current-user", TestContext.Current.CancellationToken);

        // team-2はバッチで直接200が返り所有者と判定、team-1は429の再試行上限に達して個別取得へ回り
        // そちらでも所有者と判定される(バッチ成功と個別フォールバックの併存を確認する)。
        Assert.Equal(["Alpha", "Bravo"], owned.Select(team => team.DisplayName));
        Assert.Equal(1, individualCallCount);
        Assert.Equal(3, batchCallCount);
    }

    [Fact]
    public async Task GetOwnedTeams_バッチの再試行待機中にキャンセルすると新たな要求を送らない()
    {
        int batchCallCount = 0;
        TaskCompletionSource firstBatchCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DelegateHandler handler = new(async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/joinedTeams", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"value":[{"id":"team-1","displayName":"Alpha"}]}""");
            }

            Interlocked.Increment(ref batchCallCount);
            string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(requestBody);
            // Retry-Afterを60秒と長く返し、待機中にキャンセルされることを検証できるようにする。
            var responses = doc.RootElement.GetProperty("requests").EnumerateArray().Select(req => new
            {
                id = req.GetProperty("id").GetString(),
                status = 429,
                headers = new Dictionary<string, string> { ["Retry-After"] = "60" },
                body = JsonDocument.Parse("{}").RootElement
            }).ToList();
            HttpResponseMessage response = JsonResponse(JsonSerializer.Serialize(new { responses }));
            firstBatchCompleted.TrySetResult();
            return response;
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);
        using CancellationTokenSource cancellation = new();

        Task<IReadOnlyList<TeamInfo>> request = gateway.GetOwnedTeamsAsync("current-user", cancellation.Token);
        await firstBatchCompleted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(1, batchCallCount);
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
    public async Task GetMe_IncludesErrorBodyAndCorrelationIdsInGraphException()
    {
        string? sentClientRequestId = null;
        DelegateHandler handler = new((request, _) =>
        {
            sentClientRequestId = request.Headers.GetValues("client-request-id").Single();
            HttpResponseMessage response = new(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":{\"code\":\"Authorization_RequestDenied\"}}")
            };
            response.Headers.TryAddWithoutValidation("request-id", "graph-request-id");
            response.Headers.TryAddWithoutValidation("client-request-id", sentClientRequestId);
            return Task.FromResult(response);
        });
        GraphTeamsGateway gateway = CreateGateway(handler, out _);

        GraphException exception = await Assert.ThrowsAsync<GraphException>(() =>
            gateway.GetMeAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Authorization_RequestDenied", exception.Message);
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
                "team-1", "existing-user", TestContext.Current.CancellationToken));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Contains("member write failed", exception.Message);
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
        RecordingLogger<GraphHttpClient> logger = new();
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
        RecordingLogger<GraphHttpClient> logger = new();
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
        ILogger<GraphHttpClient>? httpLogger = null)
    {
        authentication = new FakeAuthentication();
        HttpClient client = new(handler);
        GraphHttpClient http = new(new FakeHttpClientFactory(client), authentication,
            httpLogger ?? NullLogger<GraphHttpClient>.Instance);
        GraphSdkClient sdk = new(new FakeHttpClientFactory(client), authentication,
            httpLogger ?? NullLogger<GraphHttpClient>.Instance);
        return new GraphTeamsGateway(http, sdk, NullLogger<GraphTeamsGateway>.Instance);
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