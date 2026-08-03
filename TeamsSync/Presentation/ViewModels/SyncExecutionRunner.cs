using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>同期実行に必要な監査コンテキストとキャンセルを画面状態から切り離す。</summary>
internal sealed class SyncExecutionRunner(SyncExecutionCoordinator coordinator,
    IIdentifierGenerator identifierGenerator)
{
    public Task<SyncExecutionOutcome> RunAsync(SyncPlan plan, MemberListDocument document,
        string? tenantId, string? actorObjectId, IProgress<SyncProgress>? progress,
        Action? reconciliationStarting, CancellationToken cancellationToken)
    {
        SyncAuditContext auditContext = new(identifierGenerator.NewGuid(), document.FileName,
            document.ContentSha256, tenantId, actorObjectId);
        return coordinator.ExecuteAsync(plan, auditContext, progress, reconciliationStarting, cancellationToken);
    }
}
