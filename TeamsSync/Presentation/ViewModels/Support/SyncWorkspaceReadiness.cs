using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels.Support;

// 旧SyncWorkspaceCommandStateEvaluator/SyncWorkspaceTextFormatter.BuildSyncUnavailableReasonは、
// bool・nullable引数を7個前後、位置だけで渡していた(例: BuildSyncUnavailableReason(true, false, false, true, ...))。
// 呼び出し順序を間違えてもコンパイラは検出できず、実際にisSyncing/isBusyという紛らわしい引数名と
// execution.IsBusy/preview.IsBusyという渡し元の対応も一見して分からなかった。判定に必要な状態を
// このモデルへ集約し、名前付きプロパティとして参照することで取り違えを防ぐ
/// <summary>
///     同期ワークスペースが差分確認(プレビュー)・同期実行を行える状態かどうかを判定する。
///     プレビュー・実行の処理中状態、サインイン・チーム・文書・プランの有無をまとめて保持する
/// </summary>
internal sealed class SyncWorkspaceReadiness(bool externallyBusy, SyncPreviewDisplayState preview,
    SyncExecutionDisplayState execution, SyncWorkspaceContext context, SyncPlan? plan)
{
    private bool OtherOperationBusy => externallyBusy || preview.IsBusy || execution.IsBusy;

    /// <summary>差分確認(プレビュー)を実行できるかどうか</summary>
    public bool CanPreview =>
        !OtherOperationBusy && context.SignedIn && context.Team is not null && context.Document is not null;

    /// <summary>同期実行を実行できるかどうか</summary>
    public bool CanExecuteSync => !OtherOperationBusy && (plan?.CanExecute ?? false);

    /// <summary>
    ///     チームへ反映できない理由を、反映中・他処理中・未サインイン・未選択・未確認・エラーあり・
    ///     変更なしの優先順位で判定して返す。反映可能な場合は「チームに反映できます」を返す
    /// </summary>
    public string UnavailableReason
    {
        get
        {
            if (execution.IsBusy)
            {
                return "チームへ反映中です";
            }

            if (preview.IsBusy || externallyBusy)
            {
                return "別の処理が完了するまでお待ちください";
            }

            if (!context.SignedIn)
            {
                return "Microsoft 365へサインインしてください";
            }

            if (context.Team is null)
            {
                return "同期先のチームを選択してください";
            }

            if (context.Document is null)
            {
                return "メンバーリストを指定してください";
            }

            if (plan is null)
            {
                return "先に「差分を確認」を実行してください";
            }

            if (plan.HasErrors)
            {
                return "未解決ユーザーを修正してください";
            }

            if (plan.HasNoActionableChanges)
            {
                return "反映する変更はありません";
            }

            return "チームに反映できます";
        }
    }
}