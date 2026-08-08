using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Tests.Integration.Infrastructure;

public sealed class SyncResultWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TeamsSync.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private static SyncPlan CreatePlan(string teamDisplayName)
    {
        return new SyncPlan(new TeamInfo("team-id", teamDisplayName, null), [], []);
    }

    private static SyncAuditContext CreateAuditContext(Guid? executionId = null)
    {
        return new SyncAuditContext(executionId ?? Guid.NewGuid(), "members.csv", "hash",
            "tenant-1", "actor-object-id", "山田 太郎 (taro@example.com)");
    }

    // WriteCsvはplan.Operationsを基準に行を出力するため(SyncExecutorの実際の積み上げ方に合わせている)、
    // CSV出力内容を検証するテストは、resultの各要素と対応するSyncChangeを持つplanを組み立てる必要がある
    private static (SyncPlan Plan, SyncOperationsResult Result) Build(string teamDisplayName,
        IReadOnlyList<SyncOperationResult> results, bool cancelled = false, SyncMode mode = SyncMode.FullSync)
    {
        List<SyncChange> changes = results.Select((r, i) =>
            new SyncChange(r.Kind, r.DisplayName, r.Email, ChangeReason.Unspecified, $"user-{i}")).ToList();
        SyncPlan plan = new(new TeamInfo("team-id", teamDisplayName, null), changes,
            changes.Select(c => c.Email).ToList(), Mode: mode);
        return (plan, new SyncOperationsResult(results, cancelled));
    }

    private string WriteAndRead(SyncPlan plan, SyncOperationsResult result)
    {
        new SyncResultWriter(_directory).WriteAutoLog(plan, result, CreateAuditContext());
        string path = Assert.Single(Directory.GetFiles(_directory));
        return File.ReadAllText(path);
    }

    [Fact]
    public void WriteAutoLog_指定フォルダーが存在しない場合は作成する()
    {
        Assert.False(Directory.Exists(_directory));
        SyncPlan plan = CreatePlan("営業チーム");
        SyncOperationsResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        new SyncResultWriter(_directory).WriteAutoLog(plan, result, CreateAuditContext());

        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public void WriteAutoLog_実行日時と対象チーム名をファイル名にする()
    {
        SyncPlan plan = CreatePlan("営業チーム");
        SyncOperationsResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);
        DateTime before = DateTime.Now;

        new SyncResultWriter(_directory).WriteAutoLog(plan, result, CreateAuditContext());

        string fileName = Path.GetFileName(Assert.Single(Directory.GetFiles(_directory)));
        Assert.EndsWith("_営業チーム.csv", fileName);
        string[] parts = fileName.Split('_');
        DateTime timestamp = DateTime.ParseExact($"{parts[0]}_{parts[1]}_{parts[2]}", "yyyyMMdd_HHmmss_fff", null);
        Assert.InRange(timestamp, before.AddSeconds(-5), DateTime.Now.AddSeconds(5));
    }

    [Fact]
    public void WriteAutoLog_ファイル名に実行IDを含み保存したフルパスを返す()
    {
        SyncPlan plan = CreatePlan("営業チーム");
        SyncOperationsResult result = new([], false);
        Guid executionId = Guid.NewGuid();

        string path = new SyncResultWriter(_directory).WriteAutoLog(plan, result, CreateAuditContext(executionId));

        Assert.Equal(Path.GetFullPath(path), path);
        Assert.Contains(executionId.ToString("N"), Path.GetFileName(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void WriteAutoLog_同じ実行IDとチームで連続保存しても既存ファイルを上書きしない()
    {
        Guid executionId = Guid.NewGuid();
        SyncResultWriter writer = new(_directory);

        (SyncPlan plan1, SyncOperationsResult result1) = Build("営業チーム",
            [new SyncOperationResult(ChangeKind.Add, "first@example.com", true, null)]);
        string first = writer.WriteAutoLog(plan1, result1, CreateAuditContext(executionId));
        (SyncPlan plan2, SyncOperationsResult result2) = Build("営業チーム",
            [new SyncOperationResult(ChangeKind.Add, "second@example.com", true, null)]);
        string second = writer.WriteAutoLog(plan2, result2, CreateAuditContext(executionId));

        Assert.NotEqual(first, second);
        Assert.Contains("first@example.com", File.ReadAllText(first));
        Assert.Contains("second@example.com", File.ReadAllText(second));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void WriteAutoLog_チーム名にファイル名として使えない文字が含まれる場合はアンダースコアへ置き換える()
    {
        SyncPlan plan = CreatePlan("開発/QA:チーム");
        SyncOperationsResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        new SyncResultWriter(_directory).WriteAutoLog(plan, result, CreateAuditContext());

        string fileName = Path.GetFileName(Assert.Single(Directory.GetFiles(_directory)));
        Assert.EndsWith("_開発_QA_チーム.csv", fileName);
    }

    [Fact]
    public void WriteAutoLog_極端に長いチーム名はファイル名を切り詰める()
    {
        SyncPlan plan = CreatePlan(new string('あ', 300));
        SyncOperationsResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        string path = new SyncResultWriter(_directory).WriteAutoLog(plan, result, CreateAuditContext());

        string fileName = Path.GetFileName(path);
        Assert.True(fileName.Length < 200,
            $"ファイル名が長すぎます({fileName.Length}文字)。パス長超過で保存に失敗する可能性があります。");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void WriteAutoLog_実行情報セクションに実行日時操作ユーザー対象チームを出力する()
    {
        string csv = WriteAndRead(CreatePlan("営業チーム"), new SyncOperationsResult([], false));

        Assert.Contains("実行情報", csv);
        Assert.Contains("実行日時,実行ID,操作ユーザー表示名,操作ユーザーオブジェクトID,対象チーム,同期モード", csv);
        Assert.Contains("\"山田 太郎 (taro@example.com)\"", csv);
        Assert.Contains("\"actor-object-id\"", csv);
        Assert.Contains("\"営業チーム\"", csv);
    }

    [Fact]
    public void WriteAutoLog_処理前メンバー一覧セクションにプラン作成時点の全メンバーを出力する()
    {
        SyncPlan plan = new(new TeamInfo("team-id", "営業チーム", null), [], [], CurrentMembers:
        [
            new TeamMember("m-owner", "u-owner", "所有者 太郎", "owner@example.com", true),
            new TeamMember("m-1", "u-1", "一般 花子", "member@example.com", false)
        ]);

        string csv = WriteAndRead(plan, new SyncOperationsResult([], false));

        Assert.Contains("処理前メンバー一覧", csv);
        Assert.Contains("表示名,メールアドレス,区分", csv);
        Assert.Contains("\"所有者 太郎\",\"owner@example.com\",\"所有者\"", csv);
        Assert.Contains("\"一般 花子\",\"member@example.com\",\"一般\"", csv);
    }

    [Fact]
    public void WriteAutoLog_通常値はそのまま出力される()
    {
        (SyncPlan plan, SyncOperationsResult result) = Build("営業チーム",
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)]);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"営業チーム\"", csv);
        Assert.Contains("\"user1@example.com\"", csv);
        Assert.Contains("成功", csv);
    }

    [Fact]
    public void WriteAutoLog_同期操作セクションのヘッダーを日本語で出力する()
    {
        string csv = WriteAndRead(CreatePlan("営業チーム"), new SyncOperationsResult([], false));

        Assert.Contains("同期操作", csv);
        Assert.Contains("チーム,同期モード,操作,表示名,メールアドレス,結果,エラー", csv);
    }

    [Fact]
    public void WriteAutoLog_対象ユーザーの表示名を出力する()
    {
        (SyncPlan plan, SyncOperationsResult result) = Build("営業チーム",
            [new SyncOperationResult(ChangeKind.Add, "taro@example.com", true, null, "山田 太郎")]);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"山田 太郎\",\"taro@example.com\"", csv);
    }

    [Fact]
    public void WriteAutoLog_キャンセル時もプラン全体を出力して未着手の操作を未実行とする()
    {
        SyncPlan plan = new(new TeamInfo("team-id", "営業チーム", null),
        [
            new SyncChange(ChangeKind.Add, "実行済み", "done@example.com", ChangeReason.Unspecified, "done-id"),
            new SyncChange(ChangeKind.Remove, "未実行1", "pending1@example.com", ChangeReason.Unspecified, "user-1",
                "membership-1"),
            new SyncChange(ChangeKind.Add, "未実行2", "pending2@example.com", ChangeReason.Unspecified, "pending-2")
        ], ["done@example.com", "pending2@example.com"]);
        SyncOperationsResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "done@example.com", true, null, "実行済み")], true);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"実行済み\",\"done@example.com\",\"成功\"", csv);
        Assert.Contains("\"未実行1\",\"pending1@example.com\",\"未実行\",\"同期がキャンセルされたため未実行\"", csv);
        Assert.Contains("\"未実行2\",\"pending2@example.com\",\"未実行\",\"同期がキャンセルされたため未実行\"", csv);
    }

    [Fact]
    public void WriteAutoLog_失敗後も後続操作を含むプラン全体を出力する()
    {
        SyncPlan plan = new(new TeamInfo("team-id", "営業チーム", null),
        [
            new SyncChange(ChangeKind.Add, "失敗", "failed@example.com", ChangeReason.Unspecified, "failed-id"),
            new SyncChange(ChangeKind.Add, "成功", "success@example.com", ChangeReason.Unspecified, "success-id")
        ], ["failed@example.com", "success@example.com"]);
        SyncOperationsResult result = new(
        [
            new SyncOperationResult(ChangeKind.Add, "failed@example.com", false, "権限エラー", "失敗"),
            new SyncOperationResult(ChangeKind.Add, "success@example.com", true, null, "成功")
        ], false);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"失敗\",\"failed@example.com\",\"失敗\",\"権限エラー\"", csv);
        Assert.Contains("\"成功\",\"success@example.com\",\"成功\",\"\"", csv);
    }

    [Theory]
    [InlineData(SyncMode.AddOnly, "追加のみ")]
    [InlineData(SyncMode.RemoveSpecified, "指定メンバーを削除")]
    [InlineData(SyncMode.FullSync, "完全同期")]
    public void WriteAutoLog_同期モードを日本語で出力する(SyncMode mode, string expected)
    {
        (SyncPlan plan, SyncOperationsResult result) = Build("営業チーム",
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", true, null)], mode: mode);

        string csv = WriteAndRead(plan, result);

        Assert.Contains($"\"{expected}\"", csv);
    }

    [Theory]
    [InlineData(ChangeKind.Add, "追加")]
    [InlineData(ChangeKind.Remove, "削除")]
    public void WriteAutoLog_操作を日本語で出力する(ChangeKind kind, string expected)
    {
        (SyncPlan plan, SyncOperationsResult result) = Build("営業チーム",
            [new SyncOperationResult(kind, "user@example.com", true, null)]);

        string csv = WriteAndRead(plan, result);

        Assert.Contains($"\"{expected}\"", csv);
    }

    [Fact]
    public void WriteAutoLog_引用符を含む値は二重引用符へエスケープされる()
    {
        (SyncPlan plan, SyncOperationsResult result) = Build("チーム\"本社\"",
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", true, null)]);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"チーム\"\"本社\"\"\"", csv);
    }

    [Fact]
    public void WriteAutoLog_改行を含む値も出力できる()
    {
        (SyncPlan plan, SyncOperationsResult result) = Build("チーム",
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", false, "エラー1行目\nエラー2行目")]);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"エラー1行目\nエラー2行目\"", csv);
    }

    [Fact]
    public void WriteAutoLog_数式開始文字を含まない通常のメールアドレスは変更されない()
    {
        (SyncPlan plan, SyncOperationsResult result) = Build("チーム",
            [new SyncOperationResult(ChangeKind.Add, "taro.yamada@example.com", true, null)]);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"taro.yamada@example.com\"", csv);
        Assert.DoesNotContain("'taro.yamada@example.com", csv);
    }

    [Fact]
    public void AppendReconciliationResult_未反映の差分がない場合は到達済みと記録する()
    {
        SyncPlan plan = CreatePlan("営業チーム");
        SyncResultWriter writer = new(_directory);
        string path = writer.WriteAutoLog(plan, new SyncOperationsResult([], false), CreateAuditContext());
        SyncPlan remainingPlan = new(new TeamInfo("team-id", "営業チーム", null), [], []);

        writer.AppendReconciliationResult(path, remainingPlan, null);

        string csv = File.ReadAllText(path);
        Assert.Contains("最終照合結果", csv);
        Assert.Contains("目的の状態に到達しています(未反映の差分なし)", csv);
        Assert.DoesNotContain("未反映の差分\r\n操作,表示名,メールアドレス", csv);
    }

    [Fact]
    public void AppendReconciliationResult_未反映の差分がある場合はその一覧を記録する()
    {
        SyncPlan plan = CreatePlan("営業チーム");
        SyncResultWriter writer = new(_directory);
        string path = writer.WriteAutoLog(plan, new SyncOperationsResult([], false), CreateAuditContext());
        SyncPlan remainingPlan = new(new TeamInfo("team-id", "営業チーム", null),
            [new SyncChange(ChangeKind.Add, "未反映 太郎", "remaining@example.com", ChangeReason.AddToTeam, "u-1")],
            ["remaining@example.com"]);

        writer.AppendReconciliationResult(path, remainingPlan, null);

        string csv = File.ReadAllText(path);
        Assert.Contains("未反映の差分が残っています(追加待ち1件、削除待ち0件)", csv);
        Assert.Contains("未反映の差分", csv);
        Assert.Contains("\"追加\",\"未反映 太郎\",\"remaining@example.com\"", csv);
    }

    [Fact]
    public void AppendReconciliationResult_照合自体が失敗した場合はエラー内容を記録する()
    {
        SyncPlan plan = CreatePlan("営業チーム");
        SyncResultWriter writer = new(_directory);
        string path = writer.WriteAutoLog(plan, new SyncOperationsResult([], false), CreateAuditContext());

        writer.AppendReconciliationResult(path, null, new InvalidOperationException("再取得に失敗しました"));

        string csv = File.ReadAllText(path);
        Assert.Contains("照合に失敗しました: 再取得に失敗しました", csv);
    }

    [Fact]
    public void AppendReconciliationResult_既存の同期操作セクションは保持される()
    {
        (SyncPlan plan, SyncOperationsResult result) = Build("営業チーム",
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)]);
        SyncResultWriter writer = new(_directory);
        string path = writer.WriteAutoLog(plan, result, CreateAuditContext());

        writer.AppendReconciliationResult(path, new SyncPlan(plan.Team, [], []), null);

        string csv = File.ReadAllText(path);
        Assert.Contains("\"user1@example.com\"", csv);
        Assert.Contains("最終照合結果", csv);
    }
}
