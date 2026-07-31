using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace TeamsSync.Infrastructure.Logging;

/// <summary>
/// appsettings.jsonから束縛される監査ログのファイルローテーション設定。
/// </summary>
public sealed class AuditLoggingOptions
{
    /// <summary>設定ファイル上でこのオプションを束縛するセクション名。</summary>
    public const string SectionName = "AuditLogging";

    /// <summary>保持するログファイルの世代数。</summary>
    public int RetainedFileCount { get; init; } = 30;

    /// <summary>1ファイルあたりのサイズ上限(バイト)。超過するとロールオーバーする。</summary>
    public long FileSizeLimitBytes { get; init; } = 25 * 1024 * 1024;
}

/// <summary>
/// Serilogを用いた監査ログ(JSON Lines形式)の設定・登録を行う。
/// </summary>
public static class AuditLogging
{
    /// <summary>監査ログファイルの出力先ディレクトリ。</summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamsSync", "Logs");

    /// <summary>
    /// 設定値に基づきSerilogをJSON Lines形式・日次ローテーションで構成し、
    /// DIコンテナへログプロバイダーとして登録する。
    /// </summary>
    public static IServiceCollection AddAuditLogging(this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(AuditLoggingOptions.SectionName)
            .Get<AuditLoggingOptions>() ?? new AuditLoggingOptions();
        var retainedCount = Math.Clamp(options.RetainedFileCount, 1, 365);
        var fileSizeLimit = Math.Clamp(options.FileSizeLimitBytes, 1024 * 1024, 1024L * 1024 * 1024);
        Directory.CreateDirectory(LogDirectory);
        services.AddSerilog(logger => logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(new JsonFormatter(),
                Path.Combine(LogDirectory, "audit-.jsonl"),
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: fileSizeLimit,
                retainedFileCountLimit: retainedCount,
                buffered: false));
        return services;
    }
}