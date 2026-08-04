using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.ViewModels.Support;

// SyncWorkspaceViewModelから表示用テキストの組み立て・フィルター件数の計算だけを切り出した純粋な
// ヘルパー。Graph API呼び出しや進捗・キャンセルなどの同期実行ロジックとは関心事が異なるため、
// 状態を持たないstaticメソッド群として分離し、ViewModel側の計算プロパティから呼び出す形にしている
/// <summary>同期ワークスペースに表示するテキストと表示モデルを組み立てる</summary>
public static class SyncWorkspaceTextFormatter
{
    /// <summary>同期実行結果の要約テキスト(中止・一部失敗・完了)を組み立てる</summary>
    public static string BuildResultSummaryText(bool cancelled, int successCount, int failureCount)
    {
        int processedCount = successCount + failureCount;
        return cancelled
            ? $"中止しました — 処理済み {processedCount}件（成功 {successCount} / 失敗 {failureCount}）"
            : failureCount > 0
                ? $"一部失敗 — 成功 {successCount}件 / 失敗 {failureCount}件"
                : $"同期完了 — 成功 {successCount}件";
    }

    /// <summary>未反映の残り件数を示すテキストを組み立てる(未確認の場合は空文字)</summary>
    public static string BuildResultRemainingText(int? remainingCount)
    {
        return remainingCount is null ? "" : $"未反映 {remainingCount}件";
    }

    /// <summary>入力元・検出列・件数を示す要約テキストを組み立てる</summary>
    public static string BuildInputSummary(MemberListDocument? document)
    {
        return document is null
            ? ""
            : $"入力: {document.SourceName} • 列: {document.DetectedColumn} • {document.Addresses.Count}件";
    }

    /// <summary>入力アドレスの先頭数件をプレビュー表示するテキストを組み立てる</summary>
    public static string BuildInputPreview(MemberListDocument? document)
    {
        return document is null
            ? ""
            : $"先頭: {string.Join(" / ", document.Addresses.Take(5))}";
    }

    /// <summary>差分未確認時に表示する案内メッセージを、現在の状態に応じて組み立てる</summary>
    public static string BuildEmptyStateMessage(bool signedIn, TeamInfo? team, MemberListDocument? document)
    {
        return !signedIn
            ? "Microsoft 365へサインインしてください"
            : team is null
                ? "同期先のチームを選択してください"
                : document is null
                    ? "ファイルを選択して差分を確認してください"
                    : "「差分を確認」を押してください";
    }

    // モード選択コンボボックス(SyncWorkspaceViewModel.Modes)と最終確認ダイアログ
    // (WpfSyncConfirmationService)の両方で同じ文言を使うため、ここへ1本化している
    /// <summary>同期モードの画面表示用ラベルを組み立てる</summary>
    public static string BuildModeLabel(SyncMode mode)
    {
        return mode switch
        {
            SyncMode.AddOnly => "追加のみ（既存メンバーを維持）",
            SyncMode.RemoveSpecified => "指定メンバーを削除",
            SyncMode.FullSync => "完全同期（リスト外を削除）",
            _ => mode.ToString()
        };
    }

    /// <summary>差分確認中の進捗テキストを組み立てる</summary>
    public static string BuildPreviewProgressText(int previewProgressValue, int previewProgressMaximum)
    {
        return previewProgressValue > 0
            ? $"確認しています…（{previewProgressValue}/{previewProgressMaximum}件）"
            : "確認しています…";
    }

    // 差分一覧(changes)の内容から、フィルターごとの該当件数をChangeFilter.Countへ反映する。
    // 呼び出し元(ViewModel)はこの後にCollectionViewSourceのRefreshとHasErrorsの変更通知を行う
    /// <summary>差分一覧の内容から、フィルターごとの該当件数を計算して反映する</summary>
    public static void UpdateFilterCounts(IReadOnlyList<ChangeFilter> filters,
        IReadOnlyCollection<SyncChangeRowViewModel> changes)
    {
        foreach (ChangeFilter filter in filters)
        {
            filter.Count = filter.ChangesOnly
                ? changes.Count(change => ChangeFilter.MatchesChangesOnly(change.Kind))
                : filter.Kind is null
                    ? changes.Count
                    : changes.Count(change => change.Kind == filter.Kind);
        }
    }

    // メンバーリストの再指定などで差分が無効化されたときは、古い件数を0件と表示するのではなく
    // 件数表示自体を消す(未確認状態に戻す)。0件表示だと「差分を確認した結果0件だった」と
    // 誤解されるおそれがあるため
    /// <summary>すべてのフィルターの件数表示を未確認状態(null)へ戻す</summary>
    public static void ClearFilterCounts(IReadOnlyList<ChangeFilter> filters)
    {
        foreach (ChangeFilter filter in filters)
        {
            filter.Count = null;
        }
    }

    /// <summary>差分確認後のステータス文言を、未解決ユーザーの有無と変更件数に応じて組み立てる</summary>
    public static string BuildPreviewStatusText(SyncPlan plan)
    {
        if (plan.HasErrors)
        {
            return "未解決ユーザーがあります。リストを修正してください";
        }

        return plan.HasNoActionableChanges
            ? "変更はありません。すでに同期済みです"
            : $"追加 {plan.AddCount}件・削除 {plan.RemoveCount}件です。内容を確認してください";
    }

    /// <summary>同期プランから、件数サマリーと削除警告(タイトル・本文)を組み立てる</summary>
    public static PlanPresentation BuildPlan(SyncPlan plan)
    {
        string excludedSuffix = plan.ExcludedCount > 0 ? $" / 除外 {plan.ExcludedCount}" : "";
        return new PlanPresentation(
            $"追加 {plan.AddCount} / 削除 {plan.RemoveCount} / 変更なし {plan.KeepCount} / 所有者 {plan.ProtectedCount} / 未所属 {plan.NotMemberCount} / エラー {plan.ErrorCount}{excludedSuffix}",
            "削除対象があります",
            plan.Mode == SyncMode.RemoveSpecified
                ? $"入力リストで指定した一般メンバー {plan.RemoveCount}名を削除します。"
                : $"リストにない一般メンバー {plan.RemoveCount}名を削除します。");
    }

    /// <summary>実行結果から失敗した操作だけを行モデルへ変換する</summary>
    public static IReadOnlyList<SyncResultRowViewModel> BuildFailedRows(SyncOperationsResult? result,
        int maxCount = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
        return (result?.Operations ?? [])
            .Where(operation => !operation.Succeeded)
            .Take(maxCount)
            .Select(operation => new SyncResultRowViewModel(operation))
            .ToList();
    }
}

/// <summary>同期プランの件数サマリーと削除警告の表示テキスト</summary>
public sealed record PlanPresentation(string Summary, string RemovalTitle, string RemovalMessage);