using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>同期実行、監査CSV保存、実行後の最新状態取得を1つのユースケースとして調整する。</summary>
public sealed class SyncExecutionCoordinator(TeamSyncService syncService, ISyncResultWriter resultWriter)
{
    /// <summary>実行直前にプレビュー済みプランを最新のメンバー構成で再検証する。</summary>
    public Task<SyncPlanRevalidation> RevalidateAsync(SyncPlan plan,
        CancellationToken cancellationToken = default)
    {
        return syncService.RevalidatePlanAsync(plan, cancellationToken);
    }

    public async Task<SyncExecutionOutcome> ExecuteAsync(SyncPlan plan, SyncAuditContext auditContext,
        IProgress<SyncProgress>? progress = null, Action? reconciliationStarting = null,
        CancellationToken cancellationToken = default)
    {
        SyncExecutionResult execution = await syncService.ExecuteAsync(
            plan, progress, cancellationToken, auditContext);

        string resultLogPath = "";
        Exception? logSaveError = null;
        try
        {
            resultLogPath = resultWriter.WriteAutoLog(plan, execution, auditContext.ExecutionId);
        }
        catch (Exception ex)
        {
            logSaveError = ex;
        }

        SyncPlan? remainingPlan = null;
        Exception? reconciliationError = null;
        if (!execution.Cancelled)
        {
            try
            {
                reconciliationStarting?.Invoke();
                remainingPlan = await syncService.ReconcileAsync(plan, cancellationToken);
            }
            catch (Exception ex)
            {
                reconciliationError = ex;
            }
        }

        return new SyncExecutionOutcome(plan, execution, resultLogPath,
            logSaveError, remainingPlan, reconciliationError);
    }
}

/// <summary>同期ユースケースの実行結果と、独立して失敗し得る後処理の状態。</summary>
public sealed record SyncExecutionOutcome(
    SyncPlan ExecutedPlan,
    SyncExecutionResult Execution,
    string ResultLogPath,
    Exception? LogSaveError,
    SyncPlan? RemainingPlan,
    Exception? ReconciliationError);
