using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

using TeamsSync.Application.Abstractions;
using TeamsSync.Infrastructure.Authentication;
using TeamsSync.Infrastructure.Files;
using TeamsSync.Infrastructure.Graph;
using TeamsSync.Infrastructure.Settings;

namespace TeamsSync.Infrastructure;

/// <summary>
///     インフラストラクチャ層(認証・Graph API・ファイルI/O・設定)のサービスをDIコンテナへ
///     登録するための拡張メソッドを提供する。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    ///     認証・Teamsゲートウェイ・ファイル読み書き・ユーザー設定など、インフラストラクチャ層の
    ///     サービス実装をサービスコレクションへ登録する。
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EntraOptions>().Bind(configuration.GetSection(EntraOptions.SectionName));
        services.AddSingleton<IAuthenticationService, MsalAuthenticationService>();
        services.AddGraphHttpClients();
        services.AddSingleton<GraphHttpClient>();
        services.AddSingleton<GraphSdkClient>();
        services.AddSingleton<ITeamsGateway, GraphTeamsGateway>();
        services.AddSingleton<IMemberListReader, MemberListReader>();
        services.AddSingleton<IMemberTextParser, MemberTextParser>();
        services.AddSingleton<ISyncResultWriter, SyncResultWriter>();
        services.AddSingleton<IUserPreferences, JsonUserPreferences>();
        return services;
    }

    /// <summary>
    ///     Microsoft Graph呼び出し用の読み取り/書き込み<see cref="HttpClient" />を、
    ///     標準レジリエンスハンドラー(429/503のリトライを含む)付きで登録する。
    /// </summary>
    public static IServiceCollection AddGraphHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient(GraphHttpClient.ReadHttpClientName, Configure)
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .AddStandardResilienceHandler(options =>
                options.Retry.ShouldRetryAfterHeader = true);
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName, Configure)
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.ShouldRetryAfterHeader = true;
                options.Retry.DisableForUnsafeHttpMethods();
            });
        return services;

        static void Configure(HttpClient client)
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }

        static HttpMessageHandler CreatePrimaryHandler()
        {
            return new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            };
        }
    }
}