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

    private string WriteAndRead(SyncPlan plan, SyncExecutionResult result)
    {
        new SyncResultWriter(_directory).WriteAutoLog(plan, result, Guid.NewGuid());
        string path = Assert.Single(Directory.GetFiles(_directory));
        return File.ReadAllText(path);
    }

    [Fact]
    public void WriteAutoLog_指定フォルダーが存在しない場合は作成する()
    {
        Assert.False(Directory.Exists(_directory));
        SyncPlan plan = CreatePlan("営業チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        new SyncResultWriter(_directory).WriteAutoLog(plan, result, Guid.NewGuid());

        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public void WriteAutoLog_実行日時と対象チーム名をファイル名にする()
    {
        SyncPlan plan = CreatePlan("営業チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);
        DateTime before = DateTime.Now;

        new SyncResultWriter(_directory).WriteAutoLog(plan, result, Guid.NewGuid());

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
        SyncExecutionResult result = new([], false);
        Guid executionId = Guid.NewGuid();

        string path = new SyncResultWriter(_directory).WriteAutoLog(plan, result, executionId);

        Assert.Equal(Path.GetFullPath(path), path);
        Assert.Contains(executionId.ToString("N"), Path.GetFileName(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void WriteAutoLog_同じ実行IDとチームで連続保存しても既存ファイルを上書きしない()
    {
        SyncPlan plan = CreatePlan("営業チーム");
        Guid executionId = Guid.NewGuid();
        SyncResultWriter writer = new(_directory);

        string first = writer.WriteAutoLog(plan,
            new SyncExecutionResult([new SyncOperationResult(ChangeKind.Add, "first@example.com", true, null)], false),
            executionId);
        string second = writer.WriteAutoLog(plan,
            new SyncExecutionResult([new SyncOperationResult(ChangeKind.Add, "second@example.com", true, null)], false),
            executionId);

        Assert.NotEqual(first, second);
        Assert.Contains("first@example.com", File.ReadAllText(first));
        Assert.Contains("second@example.com", File.ReadAllText(second));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void WriteAutoLog_チーム名にファイル名として使えない文字が含まれる場合はアンダースコアへ置き換える()
    {
        SyncPlan plan = CreatePlan("開発/QA:チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        new SyncResultWriter(_directory).WriteAutoLog(plan, result, Guid.NewGuid());

        string fileName = Path.GetFileName(Assert.Single(Directory.GetFiles(_directory)));
        Assert.EndsWith("_開発_QA_チーム.csv", fileName);
    }

    [Fact]
    public void WriteAutoLog_通常値はそのまま出力される()
    {
        SyncPlan plan = CreatePlan("営業チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"営業チーム\"", csv);
        Assert.Contains("\"user1@example.com\"", csv);
        Assert.Contains("成功", csv);
    }

    [Fact]
    public void WriteAutoLog_ヘッダーを日本語で出力する()
    {
        string csv = WriteAndRead(CreatePlan("営業チーム"), new SyncExecutionResult([], false));

        Assert.StartsWith("チーム,同期モード,操作,表示名,メールアドレス,結果,エラー", csv);
    }

    [Fact]
    public void WriteAutoLog_対象ユーザーの表示名を出力する()
    {
        string csv = WriteAndRead(CreatePlan("営業チーム"), new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "taro@example.com", true, null, "山田 太郎")], false));

        Assert.Contains("\"山田 太郎\",\"taro@example.com\"", csv);
    }

    [Fact]
    public void WriteAutoLog_キャンセル時もプラン全体を出力して未着手の操作を未実行とする()
    {
        SyncPlan plan = new(new TeamInfo("team-id", "営業チーム", null),
        [
            new SyncChange(ChangeKind.Add, "実行済み", "done@example.com", "", "done-id"),
            new SyncChange(ChangeKind.Remove, "未実行1", "pending1@example.com", "", "user-1", "membership-1"),
            new SyncChange(ChangeKind.Add, "未実行2", "pending2@example.com", "", "pending-2")
        ], ["done@example.com", "pending2@example.com"]);
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "done@example.com", true, null, "実行済み")], true);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"実行済み\",\"done@example.com\",\"成功\"", csv);
        Assert.Contains("\"未実行1\",\"pending1@example.com\",\"未実行\",\"同期がキャンセルされたため未実行\"", csv);
        Assert.Contains("\"未実行2\",\"pending2@example.com\",\"未実行\",\"同期がキャンセルされたため未実行\"", csv);
        Assert.Equal(4, csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void WriteAutoLog_失敗後も後続操作を含むプラン全体を出力する()
    {
        SyncPlan plan = new(new TeamInfo("team-id", "営業チーム", null),
        [
            new SyncChange(ChangeKind.Add, "失敗", "failed@example.com", "", "failed-id"),
            new SyncChange(ChangeKind.Add, "成功", "success@example.com", "", "success-id")
        ], ["failed@example.com", "success@example.com"]);
        SyncExecutionResult result = new(
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
        SyncPlan plan = new(new TeamInfo("team-id", "営業チーム", null), [], [], Mode: mode);

        string csv = WriteAndRead(plan, new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", true, null)], false));

        Assert.Contains($"\"{expected}\"", csv);
    }

    [Theory]
    [InlineData(ChangeKind.Add, "追加")]
    [InlineData(ChangeKind.Remove, "削除")]
    public void WriteAutoLog_操作を日本語で出力する(ChangeKind kind, string expected)
    {
        string csv = WriteAndRead(CreatePlan("営業チーム"), new SyncExecutionResult(
            [new SyncOperationResult(kind, "user@example.com", true, null)], false));

        Assert.Contains($"\"{expected}\"", csv);
    }

    [Theory]
    [InlineData("=SUM(A1:A10)")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    public void WriteAutoLog_チーム名が数式開始文字で始まる場合はシングルクォートを付与する(string dangerous)
    {
        SyncPlan plan = CreatePlan(dangerous);
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", true, null)], false);

        string csv = WriteAndRead(plan, result);

        Assert.Contains($"\"'{dangerous}\"", csv);
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A1")]
    [InlineData("+HYPERLINK(\"http://evil.example/\")")]
    [InlineData("-2+3")]
    [InlineData("@example.com")]
    public void WriteAutoLog_メールアドレスが数式開始文字で始まる場合はシングルクォートを付与する(string dangerous)
    {
        SyncPlan plan = CreatePlan("チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, dangerous, true, null)], false);

        string csv = WriteAndRead(plan, result);

        string expected = "'" + dangerous.Replace("\"", "\"\"");
        Assert.Contains($"\"{expected}\"", csv);
    }

    [Fact]
    public void WriteAutoLog_エラー文が数式開始文字で始まる場合はシングルクォートを付与する()
    {
        SyncPlan plan = CreatePlan("チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", false, "=1+1 権限がありません")], false);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"'=1+1 権限がありません\"", csv);
    }

    [Theory]
    [InlineData("  =SUM(A1)")]
    [InlineData("\t=SUM(A1)")]
    public void WriteAutoLog_先頭の空白やタブの後に数式開始文字がある場合もシングルクォートを付与する(string dangerous)
    {
        SyncPlan plan = CreatePlan("チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, dangerous, true, null)], false);

        string csv = WriteAndRead(plan, result);

        Assert.Contains($"\"'{dangerous}\"", csv);
    }

    [Fact]
    public void WriteAutoLog_引用符を含む値は二重引用符へエスケープされる()
    {
        SyncPlan plan = CreatePlan("チーム\"本社\"");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", true, null)], false);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"チーム\"\"本社\"\"\"", csv);
    }

    [Fact]
    public void WriteAutoLog_改行を含む値も出力できる()
    {
        SyncPlan plan = CreatePlan("チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", false, "エラー1行目\nエラー2行目")], false);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"エラー1行目\nエラー2行目\"", csv);
    }

    [Fact]
    public void WriteAutoLog_数式開始文字を含まない通常のメールアドレスは変更されない()
    {
        SyncPlan plan = CreatePlan("チーム");
        SyncExecutionResult result = new(
            [new SyncOperationResult(ChangeKind.Add, "taro.yamada@example.com", true, null)], false);

        string csv = WriteAndRead(plan, result);

        Assert.Contains("\"taro.yamada@example.com\"", csv);
        Assert.DoesNotContain("'taro.yamada@example.com", csv);
    }
}
