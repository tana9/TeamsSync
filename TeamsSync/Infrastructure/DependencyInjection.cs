using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

using Polly.Retry;

using TeamsSync.Application.Abstractions;
using TeamsSync.Infrastructure.Authentication;
using TeamsSync.Infrastructure.Files;
using TeamsSync.Infrastructure.Graph;
using TeamsSync.Infrastructure.Settings;

namespace TeamsSync.Infrastructure;

/// <summary>
///     インフラストラクチャ層(認証・Graph API・ファイルI/O・設定)のサービスをDIコンテナへ
///     登録するための拡張メソッドを提供する
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    ///     認証・Teamsゲートウェイ・ファイル読み書き・ユーザー設定など、インフラストラクチャ層の
    ///     サービス実装をサービスコレクションへ登録する
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EntraOptions>().Bind(configuration.GetSection(EntraOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IIdentifierGenerator, IdentifierGenerator>();
        services.AddSingleton<IAuthenticationService, MsalAuthenticationService>();
        services.AddGraphHttpClients();
        services.AddSingleton<GraphHttpClient>();
        services.AddSingleton<GraphSdkClient>();
        // TeamMembersBatchFetcherは非ジェネリックのILoggerを受け取る構成のため、GraphTeamsGateway自身の
        // ロガーカテゴリ(ILogger<GraphTeamsGateway>)を明示的に渡すファクトリー登録にし、
        // 所有チーム判定に関するログが従来どおり同じカテゴリへまとまるようにする
        services.AddSingleton(provider => new TeamMembersBatchFetcher(
            provider.GetRequiredService<GraphHttpClient>(),
            provider.GetRequiredService<ILogger<GraphTeamsGateway>>()));
        services.AddSingleton<GraphUserSearchService>();
        services.AddSingleton<ITeamsGateway, GraphTeamsGateway>();
        services.AddSingleton<IMemberListReader, MemberListReader>();
        services.AddSingleton<IMemberTextParser, MemberTextParser>();
        services.AddSingleton<ISyncResultWriter, SyncResultWriter>();
        services.AddSingleton<IUserPreferences, JsonUserPreferences>();
        return services;
    }

    /// <summary>
    ///     Microsoft Graph呼び出し用の読み取り/書き込み<see cref="HttpClient" />を、
    ///     標準レジリエンスハンドラー(429/503のリトライを含む)付きで登録する
    /// </summary>
    public static IServiceCollection AddGraphHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient(GraphHttpClient.ReadHttpClientName, Configure)
            .ConfigurePrimaryHttpMessageHandler(CreateGraphPrimaryHandler)
            .AddStandardResilienceHandler(options =>
                options.Retry.ShouldRetryAfterHeader = true);
        services.AddHttpClient(GraphHttpClient.WriteHttpClientName, Configure)
            .ConfigurePrimaryHttpMessageHandler(CreateGraphPrimaryHandler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.ShouldRetryAfterHeader = true;
                options.Retry.DisableForUnsafeHttpMethods();
                AllowThrottlingRetryForUnsafeHttpMethods(options.Retry);
            });
        return services;

        static void Configure(HttpClient client)
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        }
    }

    // DisableForUnsafeHttpMethods()はPOST/PUT/DELETE/PATCH/CONNECTについて、429だけでなく
    // タイムアウトや503を含むあらゆる再試行対象応答を一律に再試行しないようにする。しかし429は
    // 「Graph側が処理する前に明示的にスロットリングで拒否した」ことが応答から確定しており、
    // タイムアウトや503のような「サーバー側では実際に処理済みだったかもしれない」曖昧さがないため、
    // 非冪等操作でも安全に再試行できる。DisableForUnsafeHttpMethods()の後に適用し、429の場合だけ
    // その抑止を上書きして許可する(それ以外の失敗は引き続き重複実行防止のため再試行しない)
    /// <summary>
    ///     <see cref="HttpRetryStrategyOptionsExtensions.DisableForUnsafeHttpMethods" />適用後も、
    ///     429(スロットリング)応答だけは非冪等なHTTPメソッドでも再試行を許可する
    /// </summary>
    private static void AllowThrottlingRetryForUnsafeHttpMethods(HttpRetryStrategyOptions options)
    {
        Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> shouldHandle = options.ShouldHandle;
        options.ShouldHandle = async args =>
        {
            if (args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return true;
            }

            return await shouldHandle(args).ConfigureAwait(args.Context.ContinueOnCapturedContext);
        };
    }

    /// <summary>認証ヘッダーを検証していない転送先へ引き継がないGraph用ハンドラーを作成する</summary>
    internal static SocketsHttpHandler CreateGraphPrimaryHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
        };
    }
}