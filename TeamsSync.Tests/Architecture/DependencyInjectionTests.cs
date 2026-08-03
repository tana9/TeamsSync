using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using TeamsSync.Application;
using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Services;
using TeamsSync.Infrastructure;
using TeamsSync.Presentation;

namespace TeamsSync.Tests.Architecture;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void Registrations_AreValid()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entra:ClientId"] = "00000000-0000-0000-0000-000000000000",
                ["Entra:TenantId"] = "organizations"
            })
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication().AddInfrastructure(configuration).AddPresentation();

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<IAuthenticationService>());
        Assert.NotNull(provider.GetRequiredService<ITeamsGateway>());
        Assert.NotNull(provider.GetRequiredService<IMemberListReader>());
        Assert.IsType<SyncPlanService>(provider.GetRequiredService<ISyncPlanService>());
        Assert.IsType<SyncExecutor>(provider.GetRequiredService<ISyncExecutor>());
        Assert.NotNull(provider.GetRequiredService<SyncExecutionCoordinator>());
    }

    [Fact]
    public async Task Registrations_Entra設定なしでも構築でき認証時だけ案内する()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication().AddInfrastructure(configuration).AddPresentation();

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        IAuthenticationService authentication = provider.GetRequiredService<IAuthenticationService>();
        AuthenticationConfigurationException exception =
            await Assert.ThrowsAsync<AuthenticationConfigurationException>(() =>
                authentication.GetTokenAsync(true, TestContext.Current.CancellationToken));
        Assert.Contains("ClientId", exception.Message);
        Assert.Contains("TenantIdは省略できます", exception.Message);
    }
}