using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>同期ワークスペースのコマンド実行可否と案内文を判定する。</summary>
internal sealed class SyncWorkspaceCommandStateEvaluator
{
    public bool CanPreview(bool externallyBusy, SyncPreviewDisplayState preview,
        SyncExecutionDisplayState execution, bool signedIn, TeamInfo? team, MemberListDocument? document)
    {
        return !externallyBusy && !preview.IsBusy && !execution.IsRunning && signedIn &&
               team is not null && document is not null;
    }

    public bool CanExecuteSync(bool externallyBusy, SyncPreviewDisplayState preview,
        SyncExecutionDisplayState execution, SyncPlan? plan)
    {
        return !externallyBusy && !preview.IsBusy && !execution.IsRunning && (plan?.CanExecute ?? false);
    }

    public string BuildUnavailableReason(bool externallyBusy, SyncPreviewDisplayState preview,
        SyncExecutionDisplayState execution, bool signedIn, TeamInfo? team,
        MemberListDocument? document, SyncPlan? plan)
    {
        return SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(
            execution.IsRunning, preview.IsBusy, externallyBusy, signedIn, team, document, plan);
    }
}
