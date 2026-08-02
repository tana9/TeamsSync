using Microsoft.Extensions.DependencyInjection;

using TeamsSync.Application.Services;

namespace TeamsSync.Application;

/// <summary>
///     アプリケーション層のサービスをDIコンテナへ登録するための拡張メソッドを提供する
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    ///     同期プラン作成・同期実行など、アプリケーション層のユースケースをサービスコレクションへ登録する
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<SyncPlanService>();
        services.AddSingleton<SyncExecutor>();
        services.AddSingleton<ISyncPlanService>(provider => provider.GetRequiredService<SyncPlanService>());
        services.AddSingleton<ISyncExecutor>(provider => provider.GetRequiredService<SyncExecutor>());
        services.AddSingleton<TeamsAccessService>();
        services.AddSingleton<SyncExecutionCoordinator>();
        return services;
    }
}
