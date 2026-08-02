using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using TeamsSync.Application.Abstractions;
using TeamsSync.Infrastructure;
using TeamsSync.Infrastructure.Graph;

using Xunit.Sdk;

namespace TeamsSync.Tests.Unit.Infrastructure;

public sealed class GraphHttpResilienceTests
{
    [Fact]
    public async Task GraphHttpClient_公式Graph以外の絶対URLを拒否する()
    {
        GraphHttpClient client = new(new StubHttpClientFactory(), new StubAuthenticationService(),
            NullLogger<GraphHttpClient>.Instance);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(
            "https://example.test/steal", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("許可されていないURL", exception.Message);
    }

    [Theory]
    [InlineData("https://graph.microsoft.com:444/v1.0/me")]
    [InlineData("https://user@graph.microsoft.com/v1.0/me")]
    [InlineData("http://graph.microsoft.com/v1.0/me")]
    public async Task GraphHttpClient_安全でないGraph絶対URLをトークン取得前に拒否する(string url)
    {
        GraphHttpClient client = new(new StubHttpClientFactory(), new StubAuthenticationService(),
            NullLogger<GraphHttpClient>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetAsync(url, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GraphClient_自動リダイレクトを無効化する()
    {
        using SocketsHttpHandler handler = DependencyInjection.CreateGraphPrimaryHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task ReadClient_RetryAfterに従って429を再試行する()
    {
        CountingHandler handler = new((call, _) =>
        {
            if (call == 1)
            {
                HttpResponseMessage response = new((HttpStatusCode)429);
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        ServiceCollection services = new();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.ReadHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

        using HttpResponseMessage response = await factory.CreateClient(GraphHttpClient.ReadHttpClientName)
            .GetAsync("me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task WriteClient_POSTの429はRetryAfterに従って再試行する()
    {
        CountingHandler handler = new((call, _) =>
        {
            if (call == 1)
            {
                HttpResponseMessage response = new((HttpStatusCode)429);
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        ServiceCollection services = new();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

        using HttpResponseMessage response = await factory.CreateClient(GraphHttpClient.WriteHttpClientName)
            .PostAsync("teams/team-1/members", new StringContent("{}"),
                TestContext.Current.CancellationToken);

        // 429はGraph側が処理前に明示的に拒否した応答であり重複実行の懸念がないため、
        // 非冪等なPOSTでも例外的に再試行する(DependencyInjection.AllowThrottlingRetryForUnsafeHttpMethods参照)。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task WriteClient_POSTの429再試行はRetryAfterの秒数だけ待機する()
    {
        // 標準レジリエンスハンドラーの既定バックオフ(2秒)ではなく、応答のRetry-Afterヘッダーが
        // 実際に待機時間として使われていることを、1回目と2回目の呼び出し間隔(gap)を計測して確認する。
        // 呼び出し開始からの経過時間には初回リクエストのパイプライン構築コストが乗り不安定なため、
        // ハンドラー呼び出し時刻そのものを記録し、その差分だけを検証対象にする。
        TimeSpan retryAfter = TimeSpan.FromMilliseconds(300);
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<long> callTimestampsMs = [];
        CountingHandler handler = new((call, _) =>
        {
            callTimestampsMs.Add(stopwatch.ElapsedMilliseconds);
            if (call == 1)
            {
                HttpResponseMessage response = new((HttpStatusCode)429);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        ServiceCollection services = new();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

        using HttpResponseMessage response = await factory.CreateClient(GraphHttpClient.WriteHttpClientName)
            .PostAsync("teams/team-1/members", new StringContent("{}"),
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
        long gapMs = callTimestampsMs[1] - callTimestampsMs[0];
        // Retry-After(300ms)に近い間隔であり、既定バックオフ(2秒)まで引きずられていないことを確認する。
        Assert.InRange(gapMs, retryAfter.TotalMilliseconds - 50, 1500);
    }

    [Fact]
    public async Task WriteClient_DELETEの429はRetryAfterに従って再試行する()
    {
        CountingHandler handler = new((call, _) =>
        {
            if (call == 1)
            {
                HttpResponseMessage response = new((HttpStatusCode)429);
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        ServiceCollection services = new();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

        using HttpResponseMessage response = await factory.CreateClient(GraphHttpClient.WriteHttpClientName)
            .DeleteAsync("teams/team-1/members/member-1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task WriteClient_DELETEの503を自動再試行しない()
    {
        CountingHandler handler = new((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        ServiceCollection services = new();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

        using HttpResponseMessage response = await factory.CreateClient(GraphHttpClient.WriteHttpClientName)
            .DeleteAsync("teams/team-1/members/member-1", TestContext.Current.CancellationToken);

        // 503はサーバー側で実際に処理済みだった可能性を否定できないため、429と異なり
        // 非冪等なDELETE/POSTでは引き続き再試行しない。
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task WriteClient_POSTの503を自動再試行しない()
    {
        CountingHandler handler = new((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        ServiceCollection services = new();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

        using HttpResponseMessage response = await factory.CreateClient(GraphHttpClient.WriteHttpClientName)
            .PostAsync("teams/team-1/members", new StringContent("{}"),
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class CountingHandler(
        Func<int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(Interlocked.Increment(ref _callCount), cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            throw new XunitException("HTTP送信されました");
        }
    }

    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public string? UserName => null;
        public string? TenantId => null;

        public Task<string> GetTokenAsync(bool interactive = false, CancellationToken cancellationToken = default)
        {
            throw new XunitException("トークンが取得されました");
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}