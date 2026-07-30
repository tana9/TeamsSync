using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace TeamsSync.Infrastructure.Logging;

public sealed class AuditLoggingOptions
{
    public const string SectionName = "AuditLogging";
    public int RetainedFileCount { get; init; } = 30;
    public long FileSizeLimitBytes { get; init; } = 25 * 1024 * 1024;
}

public static class AuditLogging
{
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamsSync", "Logs");

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