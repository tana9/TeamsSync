using TeamsSync.Application.Abstractions;
using TeamsSync.Infrastructure.Graph;

using Xunit.Sdk;

namespace TeamsSync.Tests.Unit.Infrastructure;

public sealed class MsalAccessTokenProviderTests
{
    [Theory]
    [InlineData("https://graph.microsoft.com:444/v1.0/me")]
    [InlineData("https://user@graph.microsoft.com/v1.0/me")]
    [InlineData("http://graph.microsoft.com/v1.0/me")]
    [InlineData("https://example.test/steal")]
    public async Task GetAuthorizationTokenAsync_許可されていないURLはトークン取得前に拒否する(string url)
    {
        MsalAccessTokenProvider provider = new(new ThrowingAuthenticationService());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.GetAuthorizationTokenAsync(new Uri(url), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAuthorizationTokenAsync_許可されたURLはトークンを取得する()
    {
        RecordingAuthenticationService authentication = new();
        MsalAccessTokenProvider provider = new(authentication);

        string token = await provider.GetAuthorizationTokenAsync(new Uri("https://graph.microsoft.com/v1.0/me"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("token", token);
        Assert.Equal(1, authentication.CallCount);
    }

    private sealed class ThrowingAuthenticationService : IAuthenticationService
    {
        public string? UserName => null;
        public string? TenantId => null;

        public Task<string> GetTokenAsync(bool interactive = false, CancellationToken cancellationToken = default)
        {
            throw new XunitException("許可されていないURLに対してトークンが取得されました");
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public int CallCount { get; private set; }
        public string? UserName => null;
        public string? TenantId => null;

        public Task<string> GetTokenAsync(bool interactive = false, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult("token");
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
