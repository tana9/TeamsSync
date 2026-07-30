namespace TeamsSync.Infrastructure.Authentication;

public sealed class EntraOptions
{
    public const string SectionName = "Entra";
    public string ClientId { get; init; } = "";
    public string TenantId { get; init; } = "organizations";
}