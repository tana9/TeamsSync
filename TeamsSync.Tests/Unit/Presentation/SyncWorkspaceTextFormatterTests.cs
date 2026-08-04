using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;
using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class SyncWorkspaceTextFormatterTests
{
    private static readonly TeamInfo Team = new("team-1", "開発", null);

    private static MemberListDocument Document(string sourceName = "members.csv", string detectedColumn = "email",
        params string[] addresses)
    {
        return new MemberListDocument(addresses, sourceName, "C:\\members.csv", new DateTime(2026, 7, 28),
            sourceName, detectedColumn);
    }

    [Fact]
    public void BuildResultSummaryText_中止時は中止メッセージと処理済み件数を返す()
    {
        string text = SyncWorkspaceTextFormatter.BuildResultSummaryText(true, 2, 1);

        Assert.Equal("中止しました — 処理済み 3件（成功 2 / 失敗 1）", text);
    }

    [Fact]
    public void BuildResultSummaryText_失敗が1件以上ある場合は一部失敗メッセージを返す()
    {
        string text = SyncWorkspaceTextFormatter.BuildResultSummaryText(false, 3, 2);

        Assert.Equal("一部失敗 — 成功 3件 / 失敗 2件", text);
    }

    [Fact]
    public void BuildResultSummaryText_失敗が0件の場合は同期完了メッセージを返す()
    {
        string text = SyncWorkspaceTextFormatter.BuildResultSummaryText(false, 5, 0);

        Assert.Equal("同期完了 — 成功 5件", text);
    }

    [Fact]
    public void BuildResultRemainingText_未確認の場合は空文字を返す()
    {
        Assert.Equal("", SyncWorkspaceTextFormatter.BuildResultRemainingText(null));
    }

    [Fact]
    public void BuildResultRemainingText_0以上の場合は未反映件数を返す()
    {
        Assert.Equal("未反映 3件", SyncWorkspaceTextFormatter.BuildResultRemainingText(3));
    }

    [Fact]
    public void BuildInputSummary_文書がnullの場合は空文字を返す()
    {
        Assert.Equal("", SyncWorkspaceTextFormatter.BuildInputSummary(null));
    }

    [Fact]
    public void BuildInputSummary_文書がある場合はソース名_列名_件数を含む()
    {
        MemberListDocument document = Document(addresses: ["a@example.com", "b@example.com"]);

        string text = SyncWorkspaceTextFormatter.BuildInputSummary(document);

        Assert.Equal("入力: members.csv • 列: email • 2件", text);
    }

    [Fact]
    public void BuildInputPreview_文書がnullの場合は空文字を返す()
    {
        Assert.Equal("", SyncWorkspaceTextFormatter.BuildInputPreview(null));
    }

    [Fact]
    public void BuildInputPreview_文書がある場合は先頭5件をスラッシュ区切りで返す()
    {
        MemberListDocument document = Document(addresses:
            ["a@example.com", "b@example.com", "c@example.com", "d@example.com", "e@example.com", "f@example.com"]);

        string text = SyncWorkspaceTextFormatter.BuildInputPreview(document);

        Assert.Equal("先頭: a@example.com / b@example.com / c@example.com / d@example.com / e@example.com", text);
    }

    [Fact]
    public void BuildEmptyStateMessage_未サインインの場合はサインインを促す()
    {
        string text = SyncWorkspaceTextFormatter.BuildEmptyStateMessage(false, Team, Document());

        Assert.Equal("Microsoft 365へサインインしてください", text);
    }

    [Fact]
    public void BuildEmptyStateMessage_サインイン済みでチーム未選択の場合はチーム選択を促す()
    {
        string text = SyncWorkspaceTextFormatter.BuildEmptyStateMessage(true, null, Document());

        Assert.Equal("同期先のチームを選択してください", text);
    }

    [Fact]
    public void BuildEmptyStateMessage_チーム選択済みで文書未選択の場合はファイル選択を促す()
    {
        string text = SyncWorkspaceTextFormatter.BuildEmptyStateMessage(true, Team, null);

        Assert.Equal("ファイルを選択して差分を確認してください", text);
    }

    [Fact]
    public void BuildEmptyStateMessage_文書選択済みの場合は差分確認を促す()
    {
        string text = SyncWorkspaceTextFormatter.BuildEmptyStateMessage(true, Team, Document());

        Assert.Equal("「差分を確認」を押してください", text);
    }

    [Fact]
    public void BuildPreviewProgressText_進捗が0より大きい場合は件数付きで表示する()
    {
        string text = SyncWorkspaceTextFormatter.BuildPreviewProgressText(3, 10);

        Assert.Equal("確認しています…（3/10件）", text);
    }

    [Fact]
    public void BuildPreviewProgressText_進捗が0の場合は件数なしで表示する()
    {
        string text = SyncWorkspaceTextFormatter.BuildPreviewProgressText(0, 10);

        Assert.Equal("確認しています…", text);
    }

    [Theory]
    [InlineData(ChangeKind.Add, true)]
    [InlineData(ChangeKind.Remove, true)]
    [InlineData(ChangeKind.Error, true)]
    [InlineData(ChangeKind.Keep, false)]
    [InlineData(ChangeKind.Protected, false)]
    [InlineData(ChangeKind.NotMember, false)]
    public void ChangeFilter_MatchesChangesOnly_追加_削除_エラーのみtrueを返す(ChangeKind kind, bool expected)
    {
        Assert.Equal(expected, ChangeFilter.MatchesChangesOnly(kind));
    }

    [Fact]
    public void UpdateFilterCounts_フィルターの種類ごとに該当件数を計算する()
    {
        ChangeFilter[] filters = new[]
        {
            new ChangeFilter("すべて", null), new ChangeFilter("追加", ChangeKind.Add),
            new ChangeFilter("削除", ChangeKind.Remove), new ChangeFilter("変更あり", null, true)
        };
        SyncChangeRowViewModel[] changes = new[]
        {
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Add, "A", "a@example.com")),
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Add, "B", "b@example.com")),
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Remove, "C", "c@example.com")),
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Keep, "D", "d@example.com")),
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Error, "E", "e@example.com"))
        };

        SyncWorkspaceTextFormatter.UpdateFilterCounts(filters, changes);

        Assert.Equal(5, filters[0].Count);
        Assert.Equal(2, filters[1].Count);
        Assert.Equal(1, filters[2].Count);
        Assert.Equal(4, filters[3].Count);
    }

    [Fact]
    public void ClearFilterCounts_全フィルターの件数を未確認状態_nullに戻す()
    {
        ChangeFilter[] filters = new[]
        {
            new ChangeFilter("すべて", null) { Count = 3 }, new ChangeFilter("追加", ChangeKind.Add) { Count = 1 }
        };

        SyncWorkspaceTextFormatter.ClearFilterCounts(filters);

        Assert.All(filters, filter => Assert.Null(filter.Count));
        Assert.All(filters, filter => Assert.False(filter.HasCount));
    }

    [Fact]
    public void BuildPlan_件数サマリーに追加_削除_変更なし_所有者_未所属_エラーの内訳を含む()
    {
        SyncPlan plan = new(Team,
        [
            new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam),
            new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveNotInInput),
            new SyncChange(ChangeKind.Keep, "維持太郎", "keep@example.com", ChangeReason.AlreadyMember),
            new SyncChange(ChangeKind.Protected, "所有太郎", "owner@example.com", ChangeReason.OwnerProtected),
            new SyncChange(ChangeKind.NotMember, "未所属太郎", "notmember@example.com", ChangeReason.NotCurrentMember),
            new SyncChange(ChangeKind.Error, "不明太郎", "unknown@example.com", ChangeReason.UserNotFound)
        ], []);

        PlanPresentation presentation = SyncWorkspaceTextFormatter.BuildPlan(plan);

        Assert.Equal("追加 1 / 削除 1 / 変更なし 1 / 所有者 1 / 未所属 1 / エラー 1", presentation.Summary);
        Assert.Equal("削除対象があります", presentation.RemovalTitle);
    }

    [Fact]
    public void BuildPlan_指定削除モードでは指定した一般メンバーを削除する旨のメッセージになる()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveSpecified)],
            [], Mode: SyncMode.RemoveSpecified);

        PlanPresentation presentation = SyncWorkspaceTextFormatter.BuildPlan(plan);

        Assert.Equal("入力リストで指定した一般メンバー 1名を削除します。", presentation.RemovalMessage);
    }

    [Theory]
    [InlineData(SyncMode.FullSync)]
    [InlineData(SyncMode.AddOnly)]
    public void BuildPlan_指定削除以外のモードではリストにない一般メンバーを削除する旨のメッセージになる(SyncMode mode)
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveNotInInput)],
            [], Mode: mode);

        PlanPresentation presentation = SyncWorkspaceTextFormatter.BuildPlan(plan);

        Assert.Equal("リストにない一般メンバー 1名を削除します。", presentation.RemovalMessage);
    }

    [Fact]
    public void BuildPreviewStatusText_未解決ユーザーがある場合は修正を促すメッセージになる()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Error, "不明太郎", "unknown@example.com", ChangeReason.UserNotFound)], []);

        string text = SyncWorkspaceTextFormatter.BuildPreviewStatusText(plan);

        Assert.Equal("未解決ユーザーがあります。リストを修正してください", text);
    }

    [Fact]
    public void BuildPreviewStatusText_追加も削除も0件の場合は同期済みである旨のメッセージになる()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Keep, "維持太郎", "keep@example.com", ChangeReason.AlreadyMember)], []);

        string text = SyncWorkspaceTextFormatter.BuildPreviewStatusText(plan);

        Assert.Equal("変更はありません。すでに同期済みです", text);
    }

    [Fact]
    public void BuildPreviewStatusText_追加または削除がある場合は件数を含むメッセージになる()
    {
        SyncPlan plan = new(Team,
        [
            new SyncChange(ChangeKind.Add, "追加太郎", "add@example.com", ChangeReason.AddToTeam),
            new SyncChange(ChangeKind.Remove, "削除太郎", "remove@example.com", ChangeReason.RemoveNotInInput)
        ], []);

        string text = SyncWorkspaceTextFormatter.BuildPreviewStatusText(plan);

        Assert.Equal("追加 1件・削除 1件です。内容を確認してください", text);
    }

    [Fact]
    public void BuildFailedRows_結果がnullの場合は空の一覧を返す()
    {
        IReadOnlyList<SyncResultRowViewModel> rows = SyncWorkspaceTextFormatter.BuildFailedRows(null);

        Assert.Empty(rows);
    }

    [Fact]
    public void BuildFailedRows_成功した操作を除外し失敗した操作だけを行モデルへ変換する()
    {
        SyncExecutionResult result = new(
        [
            new SyncOperationResult(ChangeKind.Add, "ok@example.com", true, null),
            new SyncOperationResult(ChangeKind.Add, "add-failed@example.com", false, "追加に失敗しました"),
            new SyncOperationResult(ChangeKind.Remove, "remove-failed@example.com", false, "削除に失敗しました")
        ], false);

        IReadOnlyList<SyncResultRowViewModel> rows = SyncWorkspaceTextFormatter.BuildFailedRows(result);

        Assert.Equal(2, rows.Count);
        Assert.Equal("add-failed@example.com", rows[0].Email);
        Assert.Equal("追加", rows[0].KindLabel);
        Assert.Equal("追加に失敗しました", rows[0].Error);
        Assert.Equal("remove-failed@example.com", rows[1].Email);
        Assert.Equal("削除", rows[1].KindLabel);
        Assert.Equal("削除に失敗しました", rows[1].Error);
    }

    [Fact]
    public void BuildFailedRows_表示上限を超える失敗行を生成しない()
    {
        SyncExecutionResult result = new(Enumerable.Range(1, 150)
            .Select(index => new SyncOperationResult(
                ChangeKind.Add, $"user{index}@example.com", false, "追加に失敗しました"))
            .ToList(), false);

        IReadOnlyList<SyncResultRowViewModel> rows =
            SyncWorkspaceTextFormatter.BuildFailedRows(result, 100);

        Assert.Equal(100, rows.Count);
        Assert.Equal("user1@example.com", rows[0].Email);
        Assert.Equal("user100@example.com", rows[^1].Email);
    }
}