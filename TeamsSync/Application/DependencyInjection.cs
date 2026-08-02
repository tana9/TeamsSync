using Microsoft.Extensions.DependencyInjection;

using TeamsSync.Application.Services;

namespace TeamsSync.Application;

/// <summary>
///     アプリケーション層のサービスをDIコンテナへ登録するための拡張メソッドを提供する
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    ///     アプリケーション層のサービス(<see cref="TeamSyncService" />など)をサービスコレクションへ登録する
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<TeamSyncService>();
        services.AddSingleton<TeamsAccessService>();
        services.AddSingleton<SyncExecutionCoordinator>();
        return services;
    }
}