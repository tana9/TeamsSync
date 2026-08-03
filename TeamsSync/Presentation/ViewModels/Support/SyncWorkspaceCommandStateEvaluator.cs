using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.ViewModels.Support;

// SyncWorkspaceTextFormatterと同様、状態を持たないstaticメソッド群として分離している
/// <summary>同期ワークスペースのコマンド実行可否と案内文を判定する。</summary>
internal static class SyncWorkspaceCommandStateEvaluator
{
    public static bool CanPreview(bool externallyBusy, SyncPreviewDisplayState preview,
        SyncExecutionDisplayState execution, bool signedIn, TeamInfo? team, MemberListDocument? document)
    {
        return !externallyBusy && !preview.IsBusy && !execution.IsBusy && signedIn &&
               team is not null && document is not null;
    }

    public static bool CanExecuteSync(bool externallyBusy, SyncPreviewDisplayState preview,
        SyncExecutionDisplayState execution, SyncPlan? plan)
    {
        return !externallyBusy && !preview.IsBusy && !execution.IsBusy && (plan?.CanExecute ?? false);
    }

    public static string BuildUnavailableReason(bool externallyBusy, SyncPreviewDisplayState preview,
        SyncExecutionDisplayState execution, bool signedIn, TeamInfo? team,
        MemberListDocument? document, SyncPlan? plan)
    {
        return SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(
            execution.IsBusy, preview.IsBusy, externallyBusy, signedIn, team, document, plan);
    }
}