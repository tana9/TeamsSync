using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

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
        var text = SyncWorkspaceTextFormatter.BuildResultSummaryText(true, 2, 1);

        Assert.Equal("中止しました — 処理済み 3件（成功 2 / 失敗 1）", text);
    }

    [Fact]
    public void BuildResultSummaryText_失敗が1件以上ある場合は一部失敗メッセージを返す()
    {
        var text = SyncWorkspaceTextFormatter.BuildResultSummaryText(false, 3, 2);

        Assert.Equal("一部失敗 — 成功 3件 / 失敗 2件", text);
    }

    [Fact]
    public void BuildResultSummaryText_失敗が0件の場合は同期完了メッセージを返す()
    {
        var text = SyncWorkspaceTextFormatter.BuildResultSummaryText(false, 5, 0);

        Assert.Equal("同期完了 — 成功 5件", text);
    }

    [Fact]
    public void BuildResultRemainingText_負の値の場合は空文字を返す()
    {
        Assert.Equal("", SyncWorkspaceTextFormatter.BuildResultRemainingText(-1));
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
        var document = Document(addresses: ["a@example.com", "b@example.com"]);

        var text = SyncWorkspaceTextFormatter.BuildInputSummary(document);

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
        var document = Document(addresses:
            ["a@example.com", "b@example.com", "c@example.com", "d@example.com", "e@example.com", "f@example.com"]);

        var text = SyncWorkspaceTextFormatter.BuildInputPreview(document);

        Assert.Equal("先頭: a@example.com / b@example.com / c@example.com / d@example.com / e@example.com", text);
    }

    [Fact]
    public void IsNameColumn_文書がnullの場合はfalse()
    {
        Assert.False(SyncWorkspaceTextFormatter.IsNameColumn(null));
    }

    [Theory]
    [InlineData("email", false)]
    [InlineData("name", true)]
    [InlineData("displayName", true)]
    [InlineData("氏名", true)]
    [InlineData("姓名", true)]
    [InlineData("名前", true)]
    [InlineData("display_name", true)]
    [InlineData("display name", true)]
    public void IsNameColumn_検出列名が氏名系かどうかを正規化して判定する(string detectedColumn, bool expected)
    {
        var document = Document(detectedColumn: detectedColumn, addresses: ["a@example.com"]);

        Assert.Equal(expected, SyncWorkspaceTextFormatter.IsNameColumn(document));
    }

    [Fact]
    public void BuildEmptyStateMessage_未サインインの場合はサインインを促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildEmptyStateMessage(false, Team, Document());

        Assert.Equal("Microsoft 365へサインインしてください", text);
    }

    [Fact]
    public void BuildEmptyStateMessage_サインイン済みでチーム未選択の場合はチーム選択を促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildEmptyStateMessage(true, null, Document());

        Assert.Equal("同期先のチームを選択してください", text);
    }

    [Fact]
    public void BuildEmptyStateMessage_チーム選択済みで文書未選択の場合はファイル選択を促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildEmptyStateMessage(true, Team, null);

        Assert.Equal("ファイルを選択して差分を確認してください", text);
    }

    [Fact]
    public void BuildEmptyStateMessage_文書選択済みの場合は差分確認を促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildEmptyStateMessage(true, Team, Document());

        Assert.Equal("「差分を確認」を押してください", text);
    }

    [Fact]
    public void BuildPreviewProgressText_進捗が0より大きい場合は件数付きで表示する()
    {
        var text = SyncWorkspaceTextFormatter.BuildPreviewProgressText(3, 10);

        Assert.Equal("確認しています…（3/10件）", text);
    }

    [Fact]
    public void BuildPreviewProgressText_進捗が0の場合は件数なしで表示する()
    {
        var text = SyncWorkspaceTextFormatter.BuildPreviewProgressText(0, 10);

        Assert.Equal("確認しています…", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_同期実行中の場合はその旨を返す()
    {
        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(true, false, false, true, Team, Document(),
            PlanWithAdd());

        Assert.Equal("同期を実行中です", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_自身がbusyの場合は待機を促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, true, false, true, Team, Document(),
            PlanWithAdd());

        Assert.Equal("別の処理が完了するまでお待ちください", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_外部要因でbusyの場合は待機を促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, false, true, true, Team, Document(),
            PlanWithAdd());

        Assert.Equal("別の処理が完了するまでお待ちください", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_未サインインの場合はサインインを促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, false, false, false, Team,
            Document(), PlanWithAdd());

        Assert.Equal("Microsoft 365へサインインしてください", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_チーム未選択の場合はチーム選択を促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, false, false, true, null,
            Document(), PlanWithAdd());

        Assert.Equal("同期先のチームを選択してください", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_文書未選択の場合はメンバーリスト指定を促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, false, false, true, Team, null,
            PlanWithAdd());

        Assert.Equal("メンバーリストを指定してください", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_プラン未確認の場合は差分確認を促す()
    {
        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, false, false, true, Team,
            Document(), null);

        Assert.Equal("先に「差分を確認」を実行してください", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_プランにエラーがある場合は未解決ユーザーの修正を促す()
    {
        var plan = new SyncPlan(Team, [new SyncChange(ChangeKind.Error, "不明", "unknown@example.com", "未解決")],
            ["unknown@example.com"]);

        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, false, false, true, Team,
            Document(), plan);

        Assert.Equal("未解決ユーザーを修正してください", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_変更が0件の場合は実行する変更がない旨を返す()
    {
        var plan = new SyncPlan(Team, [new SyncChange(ChangeKind.Keep, "既存", "keep@example.com", "変更なし")],
            ["keep@example.com"]);

        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, false, false, true, Team,
            Document(), plan);

        Assert.Equal("実行する変更はありません", text);
    }

    [Fact]
    public void BuildSyncUnavailableReason_実行可能な場合は実行可能メッセージを返す()
    {
        var text = SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(false, false, false, true, Team,
            Document(), PlanWithAdd());

        Assert.Equal("同期を実行できます", text);
    }

    [Fact]
    public void UpdateFilterCounts_フィルターの種類ごとに該当件数を計算する()
    {
        var filters = new[]
        {
            new ChangeFilter("すべて", null),
            new ChangeFilter("追加", ChangeKind.Add),
            new ChangeFilter("削除", ChangeKind.Remove),
            new ChangeFilter("変更あり", null, true)
        };
        var changes = new[]
        {
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Add, "A", "a@example.com", "")),
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Add, "B", "b@example.com", "")),
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Remove, "C", "c@example.com", "")),
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Keep, "D", "d@example.com", "")),
            new SyncChangeRowViewModel(new SyncChange(ChangeKind.Error, "E", "e@example.com", ""))
        };

        SyncWorkspaceTextFormatter.UpdateFilterCounts(filters, changes);

        Assert.Equal(5, filters[0].Count);
        Assert.Equal(2, filters[1].Count);
        Assert.Equal(1, filters[2].Count);
        Assert.Equal(4, filters[3].Count);
    }

    [Fact]
    public void ClearFilterCounts_全フィルターの件数を未確認状態_マイナス1に戻す()
    {
        var filters = new[]
        {
            new ChangeFilter("すべて", null) { Count = 3 },
            new ChangeFilter("追加", ChangeKind.Add) { Count = 1 }
        };

        SyncWorkspaceTextFormatter.ClearFilterCounts(filters);

        Assert.All(filters, filter => Assert.Equal(-1, filter.Count));
    }

    private static SyncPlan PlanWithAdd()
    {
        return new SyncPlan(Team, [new SyncChange(ChangeKind.Add, "新規", "new@example.com", "追加")],
            ["new@example.com"]);
    }
}
