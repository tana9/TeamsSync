using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>同期ドメインモデルを画面表示用の要約と行モデルへ変換する。</summary>
internal static class SyncWorkspacePresentation
{
    internal static PlanPresentation BuildPlan(SyncPlan plan) => new(
        $"追加 {plan.AddCount} / 削除 {plan.RemoveCount} / 変更なし {plan.KeepCount} / 所有者 {plan.ProtectedCount} / 未所属 {plan.NotMemberCount} / エラー {plan.ErrorCount}",
        "削除対象があります",
        plan.Mode == SyncMode.RemoveSpecified
            ? $"入力リストで指定した一般メンバー {plan.RemoveCount}名を削除します。"
            : $"リストにない一般メンバー {plan.RemoveCount}名を削除します。");

    internal static IReadOnlyList<SyncResultRowViewModel> BuildFailedRows(SyncExecutionResult? result) =>
        (result?.Operations ?? [])
        .Where(operation => !operation.Succeeded)
        .Select(operation => new SyncResultRowViewModel(operation))
        .ToList();
}

internal sealed record PlanPresentation(string Summary, string RemovalTitle, string RemovalMessage);
