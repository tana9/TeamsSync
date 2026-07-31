using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TeamsSync.Application.Abstractions;
using TeamsSync.Infrastructure;
using TeamsSync.Infrastructure.Graph;

namespace TeamsSync.Tests.Unit.Infrastructure;

public sealed class GraphHttpResilienceTests
{
    [Fact]
    public async Task GraphHttpClient_公式Graph以外の絶対URLを拒否する()
    {
        var client = new GraphHttpClient(new StubHttpClientFactory(), new StubAuthenticationService(),
            NullLogger<GraphHttpClient>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(
            "https://example.test/steal", TestContext.Current.CancellationToken));

        Assert.Contains("許可されていないURL", exception.Message);
    }

    [Fact]
    public async Task ReadClient_RetryAfterに従って429を再試行する()
    {
        var handler = new CountingHandler((call, _) =>
        {
            if (call == 1)
            {
                var response = new HttpResponseMessage((HttpStatusCode)429);
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.ReadHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var response = await factory.CreateClient(GraphHttpClient.ReadHttpClientName)
            .GetAsync("me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task WriteClient_POSTの429を自動再試行しない()
    {
        var handler = new CountingHandler((call, _) =>
        {
            if (call == 1)
            {
                var response = new HttpResponseMessage((HttpStatusCode)429);
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var response = await factory.CreateClient(GraphHttpClient.WriteHttpClientName)
            .PostAsync("teams/team-1/members", new StringContent("{}"),
                TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task WriteClient_DELETEの503を自動再試行しない()
    {
        var handler = new CountingHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGraphHttpClients();
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var response = await factory.CreateClient(GraphHttpClient.WriteHttpClientName)
            .DeleteAsync("teams/team-1/members/member-1", TestContext.Current.CancellationToken);

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
        public HttpClient CreateClient(string name) => throw new Xunit.Sdk.XunitException("HTTP送信されました");
    }

    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public string? UserName => null;
        public string? TenantId => null;
        public Task<string> GetTokenAsync(bool interactive = false, CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("トークンが取得されました");
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
