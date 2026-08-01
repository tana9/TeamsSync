using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Tests.Integration.Infrastructure;

public sealed class SyncResultWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TeamsSync.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static SyncPlan CreatePlan(string teamDisplayName)
    {
        return new SyncPlan(new TeamInfo("team-id", teamDisplayName, null), [], []);
    }

    private string WriteAndRead(SyncPlan plan, SyncExecutionResult result)
    {
        new SyncResultWriter(_directory).WriteAutoLog(plan, result);
        var path = Assert.Single(Directory.GetFiles(_directory));
        return File.ReadAllText(path);
    }

    [Fact]
    public void WriteAutoLog_指定フォルダーが存在しない場合は作成する()
    {
        Assert.False(Directory.Exists(_directory));
        var plan = CreatePlan("営業チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        new SyncResultWriter(_directory).WriteAutoLog(plan, result);

        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public void WriteAutoLog_実行日時と対象チーム名をファイル名にする()
    {
        var plan = CreatePlan("営業チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);
        var before = DateTime.Now;

        new SyncResultWriter(_directory).WriteAutoLog(plan, result);

        var fileName = Path.GetFileName(Assert.Single(Directory.GetFiles(_directory)));
        Assert.EndsWith("_営業チーム.csv", fileName);
        var parts = fileName.Split('_');
        var timestamp = DateTime.ParseExact($"{parts[0]}_{parts[1]}", "yyyyMMdd_HHmmss", null);
        Assert.InRange(timestamp, before.AddSeconds(-5), DateTime.Now.AddSeconds(5));
    }

    [Fact]
    public void WriteAutoLog_チーム名にファイル名として使えない文字が含まれる場合はアンダースコアへ置き換える()
    {
        var plan = CreatePlan("開発/QA:チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        new SyncResultWriter(_directory).WriteAutoLog(plan, result);

        var fileName = Path.GetFileName(Assert.Single(Directory.GetFiles(_directory)));
        Assert.EndsWith("_開発_QA_チーム.csv", fileName);
    }

    [Fact]
    public void WriteAutoLog_通常値はそのまま出力される()
    {
        var plan = CreatePlan("営業チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user1@example.com", true, null)], false);

        var csv = WriteAndRead(plan, result);

        Assert.Contains("\"営業チーム\"", csv);
        Assert.Contains("\"user1@example.com\"", csv);
        Assert.Contains("成功", csv);
    }

    [Theory]
    [InlineData("=SUM(A1:A10)")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    public void WriteAutoLog_チーム名が数式開始文字で始まる場合はシングルクォートを付与する(string dangerous)
    {
        var plan = CreatePlan(dangerous);
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", true, null)], false);

        var csv = WriteAndRead(plan, result);

        Assert.Contains($"\"'{dangerous}\"", csv);
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A1")]
    [InlineData("+HYPERLINK(\"http://evil.example/\")")]
    [InlineData("-2+3")]
    [InlineData("@example.com")]
    public void WriteAutoLog_メールアドレスが数式開始文字で始まる場合はシングルクォートを付与する(string dangerous)
    {
        var plan = CreatePlan("チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, dangerous, true, null)], false);

        var csv = WriteAndRead(plan, result);

        var expected = "'" + dangerous.Replace("\"", "\"\"");
        Assert.Contains($"\"{expected}\"", csv);
    }

    [Fact]
    public void WriteAutoLog_エラー文が数式開始文字で始まる場合はシングルクォートを付与する()
    {
        var plan = CreatePlan("チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", false, "=1+1 権限がありません")], false);

        var csv = WriteAndRead(plan, result);

        Assert.Contains("\"'=1+1 権限がありません\"", csv);
    }

    [Theory]
    [InlineData("  =SUM(A1)")]
    [InlineData("\t=SUM(A1)")]
    public void WriteAutoLog_先頭の空白やタブの後に数式開始文字がある場合もシングルクォートを付与する(string dangerous)
    {
        var plan = CreatePlan("チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, dangerous, true, null)], false);

        var csv = WriteAndRead(plan, result);

        Assert.Contains($"\"'{dangerous}\"", csv);
    }

    [Fact]
    public void WriteAutoLog_引用符を含む値は二重引用符へエスケープされる()
    {
        var plan = CreatePlan("チーム\"本社\"");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", true, null)], false);

        var csv = WriteAndRead(plan, result);

        Assert.Contains("\"チーム\"\"本社\"\"\"", csv);
    }

    [Fact]
    public void WriteAutoLog_改行を含む値も出力できる()
    {
        var plan = CreatePlan("チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "user@example.com", false, "エラー1行目\nエラー2行目")], false);

        var csv = WriteAndRead(plan, result);

        Assert.Contains("\"エラー1行目\nエラー2行目\"", csv);
    }

    [Fact]
    public void WriteAutoLog_数式開始文字を含まない通常のメールアドレスは変更されない()
    {
        var plan = CreatePlan("チーム");
        var result = new SyncExecutionResult(
            [new SyncOperationResult(ChangeKind.Add, "taro.yamada@example.com", true, null)], false);

        var csv = WriteAndRead(plan, result);

        Assert.Contains("\"taro.yamada@example.com\"", csv);
        Assert.DoesNotContain("'taro.yamada@example.com", csv);
    }
}
