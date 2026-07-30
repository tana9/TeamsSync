namespace TeamsSync.Domain.Teams;

public sealed record TeamInfo(string Id, string DisplayName, string? Description)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed record TeamMember(string MembershipId, string UserId, string DisplayName, string Email, bool IsOwner);

public sealed record DirectoryUser(string Id, string DisplayName, string UserPrincipalName, string? Mail);

public enum ChangeKind
{
    Add,
    Remove,
    Keep,
    Protected,
    Error
}

public enum SyncMode
{
    AddOnly,
    FullSync
}

public sealed record SyncChange(
    ChangeKind Kind,
    string DisplayName,
    string Email,
    string Detail,
    string? UserId = null,
    string? MembershipId = null);

public sealed record TeamMembershipSnapshot(string MembershipId, string UserId, bool IsOwner);

public sealed record SyncPlan(
    TeamInfo Team,
    IReadOnlyList<SyncChange> Changes,
    IReadOnlyList<string> InputAddresses,
    int CurrentMemberCount = 0,
    IReadOnlyList<TeamMembershipSnapshot>? MembershipSnapshot = null,
    SyncMode Mode = SyncMode.FullSync)
{
    public int AddCount => Changes.Count(x => x.Kind == ChangeKind.Add);
    public int RemoveCount => Changes.Count(x => x.Kind == ChangeKind.Remove);
    public int KeepCount => Changes.Count(x => x.Kind == ChangeKind.Keep);
    public int ProtectedCount => Changes.Count(x => x.Kind == ChangeKind.Protected);
    public int ErrorCount => Changes.Count(x => x.Kind == ChangeKind.Error);
    public bool HasErrors => ErrorCount > 0;
    public double RemoveRatio => CurrentMemberCount == 0 ? 0 : (double)RemoveCount / CurrentMemberCount;
    public bool IsLargeRemoval => RemoveCount >= 10 || RemoveRatio >= 0.5;
}

public sealed record SyncPlanRevalidation(bool IsCurrent, SyncPlan LatestPlan);

public sealed record MemberListDocument(
    IReadOnlyList<string> Addresses,
    string FileName,
    string FullPath,
    DateTime LastModified,
    string SourceName,
    string DetectedColumn,
    string ContentSha256 = "");

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

public sealed record SyncOperationResult(ChangeKind Kind, string Email, bool Succeeded, string? Error);

public sealed record SyncExecutionResult(IReadOnlyList<SyncOperationResult> Operations, bool Cancelled)
{
    public int SuccessCount => Operations.Count(x => x.Succeeded);
    public int FailureCount => Operations.Count(x => !x.Succeeded);
}