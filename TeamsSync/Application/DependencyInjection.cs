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
        services.AddSingleton<ISyncPlanService, SyncPlanService>();
        services.AddSingleton<ISyncExecutor, SyncExecutor>();
        services.AddSingleton<TeamsAccessService>();
        services.AddSingleton<SyncExecutionCoordinator>();
        return services;
    }
}