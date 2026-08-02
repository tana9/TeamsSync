using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Models;

public sealed record SyncPlanRevalidation(bool IsCurrent, SyncPlan LatestPlan);

public sealed record MemberListDocument(
    IReadOnlyList<string> Addresses,
    string FileName,
    string FullPath,
    DateTime LastModified,
    string SourceName,
    string DetectedColumn,
    string ContentSha256 = "",
    bool IsNameColumn = false);

public sealed record SyncAuditContext(
    Guid ExecutionId,
    string InputFileName,
    string InputFileSha256,
    string? TenantId = null,
    string? ActorObjectId = null);

public sealed record SyncProgress(int Completed, int Total, ChangeKind Kind, string Email)
{
    public int Percentage => Total == 0 ? 0 : (int)Math.Round(Completed * 100d / Total);
}

public sealed record SyncOperationResult(ChangeKind Kind, string Email, bool Succeeded, string? Error,
    string DisplayName = "");

public sealed record SyncExecutionResult(IReadOnlyList<SyncOperationResult> Operations, bool Cancelled)
{
    public int SuccessCount => Operations.Count(x => x.Succeeded);
    public int FailureCount => Operations.Count(x => !x.Succeeded);
}
