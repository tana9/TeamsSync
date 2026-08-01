using Microsoft.Kiota.Abstractions.Authentication;

using TeamsSync.Application.Abstractions;

namespace TeamsSync.Infrastructure.Graph;

internal sealed class MsalAccessTokenProvider(IAuthenticationService authentication) : IAccessTokenProvider
{
    public AllowedHostsValidator AllowedHostsValidator { get; } = new(["graph.microsoft.com"]);

    public Task<string> GetAuthorizationTokenAsync(Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        return !AllowedHostsValidator.IsUrlHostValid(uri)
            ? throw new InvalidOperationException("Microsoft Graph以外のホストへトークンを送信できません。")
            : authentication.GetTokenAsync(cancellationToken: cancellationToken);
    }
}