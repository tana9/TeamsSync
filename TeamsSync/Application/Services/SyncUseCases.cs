using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>同期プランの作成・再検証・再計画を提供する</summary>
public interface ISyncPlanService
{
    /// <inheritdoc cref="SyncPlanService.BuildPlanAsync" />
    Task<SyncPlan> BuildPlanAsync(TeamInfo team, IReadOnlyList<string> addresses,
        SyncMode mode = SyncMode.FullSync, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SyncPlanService.RevalidatePlanAsync" />
    Task<SyncPlanRevalidation> RevalidatePlanAsync(SyncPlan preview,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SyncPlanService.ReconcileAsync" />
    Task<SyncPlan> ReconcileAsync(SyncPlan executedPlan,
        CancellationToken cancellationToken = default);
}

/// <summary>検証済み同期プランの実行を提供する</summary>
public interface ISyncExecutor
{
    /// <inheritdoc cref="SyncExecutor.ExecuteAsync" />
    Task<SyncOperationsResult> ExecuteAsync(SyncPlan plan,
        IProgress<SyncProgress>? progress = null, SyncAuditContext? auditContext = null,
        CancellationToken cancellationToken = default);
}