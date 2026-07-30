using Microsoft.Extensions.DependencyInjection;
using TeamsSync.Application.Services;

namespace TeamsSync.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<TeamSyncService>();
        return services;
    }
}