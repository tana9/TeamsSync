using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>検証済みの同期プランに含まれる追加・削除操作を実行する</summary>
public sealed class SyncExecutor(ITeamsGateway teamsGateway, ILogger<SyncExecutor>? logger = null,
    IIdentifierGenerator? identifierGenerator = null) : ISyncExecutor
{
    private static readonly TimeSpan OperationThrottleDelay = TimeSpan.FromMilliseconds(300);
    private readonly ILogger<SyncExecutor> _logger = logger ?? NullLogger<SyncExecutor>.Instance;
    private readonly IIdentifierGenerator _identifierGenerator = identifierGenerator ?? new IdentifierGenerator();

    /// <summary>同期プランの追加・削除操作を順に実行する</summary>
    public async Task<SyncOperationsResult> ExecuteAsync(SyncPlan plan,
        IProgress<SyncProgress>? progress = null, SyncAuditContext? auditContext = null,
        CancellationToken cancellationToken = default)
    {
        plan.EnsureExecutable();
        using IDisposable? auditScope = BeginAuditScope(plan, auditContext);
        return await ExecuteOperationsAsync(plan, progress, cancellationToken);
    }

    private IDisposable? BeginAuditScope(SyncPlan plan, SyncAuditContext? auditContext)
    {
        SyncAuditContext context = auditContext ?? new SyncAuditContext(_identifierGenerator.NewGuid(), "", "");
        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["ExecutionId"] = context.ExecutionId,
            ["TeamId"] = plan.Team.Id,
            ["InputFileName"] = context.InputFileName,
            ["InputFileSha256"] = context.InputFileSha256,
            ["TenantId"] = context.TenantId,
            ["ActorObjectId"] = context.ActorObjectId,
            ["SyncMode"] = plan.Mode
        });
    }

    private async Task<SyncOperationsResult> ExecuteOperationsAsync(SyncPlan plan,
        IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
    {
        List<SyncOperationResult> results = [];
        IReadOnlyList<SyncChange> operations = plan.Operations;
        _logger.LogInformation(
            "SyncStarted Add={AddCount} Remove={RemoveCount} Keep={KeepCount} Protected={ProtectedCount}",
            plan.AddCount, plan.RemoveCount, plan.KeepCount, plan.ProtectedCount);

        SyncOperationsResult BuildCancelledResult()
        {
            LogSyncCancelled(results, operations.Count);
            return new SyncOperationsResult(results, true);
        }

        for (int i = 0; i < operations.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return BuildCancelledResult();
            }

            SyncChange change = operations[i];
            progress?.Report(new SyncProgress(i, operations.Count, change.Kind, change.Email));
            bool cancelled = await ExecuteChangeAsync(plan.Team.Id, change, i, operations.Count, progress, results,
                cancellationToken);
            if (cancelled)
            {
                return BuildCancelledResult();
            }
        }

        SyncOperationsResult result = new(results, false);
        _logger.LogInformation("SyncCompleted Success={SuccessCount} Failure={FailureCount}",
            result.SuccessCount, result.FailureCount);
        return result;
    }

    private async Task<bool> ExecuteChangeAsync(string teamId, SyncChange change, int index, int totalOperations,
        IProgress<SyncProgress>? progress, List<SyncOperationResult> results, CancellationToken cancellationToken)
    {
        string? targetObjectId = change.TargetObjectId;
        _logger.LogInformation("MemberOperationStarted Kind={Kind} TargetObjectId={TargetObjectId}",
            change.Kind, targetObjectId);
        try
        {
            if (change.Kind == ChangeKind.Add)
            {
                await teamsGateway.AddMemberAsync(teamId, targetObjectId!, cancellationToken);
            }
            else
            {
                await teamsGateway.RemoveMemberAsync(teamId, targetObjectId!, cancellationToken);
            }

            results.Add(new SyncOperationResult(change.Kind, change.Email, true, null, change.DisplayName));
            _logger.LogInformation("MemberOperationSucceeded Kind={Kind} TargetObjectId={TargetObjectId}",
                change.Kind, targetObjectId);
            progress?.Report(new SyncProgress(index + 1, totalOperations, change.Kind, change.Email));
            if (index < totalOperations - 1)
            {
                await Task.Delay(OperationThrottleDelay, cancellationToken);
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graphへの要求送信後にキャンセルされた場合、サーバー側で実際に成功しているかどうかは
            // ここでは判別できない。記録から欠落させると「未実行」と誤認されるため、
            // 状態不明として明示的に記録し、後続のReconcileAsyncによる実際の状態確認に委ねる
            results.Add(new SyncOperationResult(change.Kind, change.Email, false,
                "キャンセルされたため、Teams側で実際に成功したかどうか不明です。最新状態を確認してください",
                change.DisplayName, true));
            _logger.LogWarning(
                "MemberOperationUncertain Kind={Kind} TargetObjectId={TargetObjectId}",
                change.Kind, targetObjectId);
            return true;
        }
        catch (Exception ex)
        {
            results.Add(new SyncOperationResult(change.Kind, change.Email, false, ex.Message, change.DisplayName));
            _logger.LogWarning(
                "MemberOperationFailed Kind={Kind} TargetObjectId={TargetObjectId} ErrorType={ErrorType}",
                change.Kind, targetObjectId, ex.GetType().Name);
            return false;
        }
    }

    private void LogSyncCancelled(IReadOnlyList<SyncOperationResult> results, int totalOperations)
    {
        _logger.LogWarning(
            "SyncCancelled Success={SuccessCount} Failure={FailureCount} Uncertain={UncertainCount} " +
            "Unexecuted={UnexecutedCount}",
            results.Count(result => result.Succeeded), results.Count(result => !result.Succeeded),
            results.Count(result => result.Uncertain), Math.Max(0, totalOperations - results.Count));
    }
}