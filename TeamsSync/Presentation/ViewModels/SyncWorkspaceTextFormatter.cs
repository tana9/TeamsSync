using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels;

// SyncWorkspaceViewModelから表示用テキストの組み立て・フィルター件数の計算だけを切り出した純粋な
// ヘルパー。Graph API呼び出しや進捗・キャンセルなどの同期実行ロジックとは関心事が異なるため、
// 状態を持たないstaticメソッド群として分離し、ViewModel側の計算プロパティから呼び出す形にしている。
public static class SyncWorkspaceTextFormatter
{
    public static string BuildResultSummaryText(bool cancelled, int successCount, int failureCount)
    {
        var processedCount = successCount + failureCount;
        return cancelled
            ? $"中止しました — 処理済み {processedCount}件（成功 {successCount} / 失敗 {failureCount}）"
            : failureCount > 0
                ? $"一部失敗 — 成功 {successCount}件 / 失敗 {failureCount}件"
                : $"同期完了 — 成功 {successCount}件";
    }

    public static string BuildResultRemainingText(int remainingCount)
    {
        return remainingCount < 0 ? "" : $"未反映 {remainingCount}件";
    }

    public static string BuildInputSummary(MemberListDocument? document)
    {
        return document is null
            ? ""
            : $"入力: {document.SourceName} • 列: {document.DetectedColumn} • {document.Addresses.Count}件";
    }

    public static string BuildInputPreview(MemberListDocument? document)
    {
        return document is null
            ? ""
            : $"先頭: {string.Join(" / ", document.Addresses.Take(5))}";
    }

    public static bool IsNameColumn(MemberListDocument? document)
    {
        return document is not null &&
               new[] { "name", "displayname", "氏名", "姓名", "名前" }.Contains(
                   document.DetectedColumn.Replace("_", "").Replace(" ", "").ToLowerInvariant());
    }

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

    public static string BuildPreviewProgressText(int previewProgressValue, int previewProgressMaximum)
    {
        return previewProgressValue > 0
            ? $"確認しています…（{previewProgressValue}/{previewProgressMaximum}件）"
            : "確認しています…";
    }

    public static string BuildSyncUnavailableReason(bool isSyncing, bool isBusy, bool externallyBusy,
        bool signedIn, TeamInfo? team, MemberListDocument? document, SyncPlan? plan)
    {
        if (isSyncing) return "同期を実行中です";
        if (isBusy || externallyBusy) return "別の処理が完了するまでお待ちください";
        if (!signedIn) return "Microsoft 365へサインインしてください";
        if (team is null) return "同期先のチームを選択してください";
        if (document is null) return "メンバーリストを指定してください";
        if (plan is null) return "先に「差分を確認」を実行してください";
        if (plan.HasErrors) return "未解決ユーザーを修正してください";
        if (plan.AddCount + plan.RemoveCount == 0) return "実行する変更はありません";
        return "同期を実行できます";
    }

    // 差分一覧(changes)の内容から、フィルターごとの該当件数をChangeFilter.Countへ反映する。
    // 呼び出し元(ViewModel)はこの後にCollectionViewSourceのRefreshとHasErrorsの変更通知を行う。
    public static void UpdateFilterCounts(IReadOnlyList<ChangeFilter> filters,
        IReadOnlyCollection<SyncChangeRowViewModel> changes)
    {
        foreach (var filter in filters)
            filter.Count = filter.ChangesOnly
                ? changes.Count(change => change.Kind is ChangeKind.Add or ChangeKind.Remove or ChangeKind.Error)
                : filter.Kind is null ? changes.Count : changes.Count(change => change.Kind == filter.Kind);
    }

    // メンバーリストの再指定などで差分が無効化されたときは、古い件数を0件と表示するのではなく
    // 件数表示自体を消す(未確認状態に戻す)。0件表示だと「差分を確認した結果0件だった」と
    // 誤解されるおそれがあるため。
    public static void ClearFilterCounts(IReadOnlyList<ChangeFilter> filters)
    {
        foreach (var filter in filters)
            filter.Count = -1;
    }
}
