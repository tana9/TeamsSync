using TeamsSync.Application.Models;
using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Tests.Unit.Application;

public sealed class MemberTextParserTests
{
    private readonly MemberTextParser _parser = new();

    [Fact]
    public void Parse_キャンセル済みトークンでは解析を開始しない()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => _parser.Parse("user@example.com", cancellation.Token));
    }

    [Fact]
    public void Parse_改行と空行を正規化し重複を除去する()
    {
        MemberListDocument document = _parser.Parse(
            " user@example.com\r\n\r\nUSER@example.com\n山田　太郎\r佐藤 花子 ", TestContext.Current.CancellationToken);

        Assert.Equal(
            ["user@example.com", "山田　太郎", "佐藤 花子"],
            document.Addresses);
        Assert.Equal("貼り付け入力.txt", document.FileName);
        Assert.Equal(64, document.ContentSha256.Length);
    }

    [Fact]
    public void Parse_正規化後の内容が同じなら同じハッシュを返す()
    {
        MemberListDocument first = _parser.Parse("user@example.com\r\n山田 太郎", TestContext.Current.CancellationToken);
        MemberListDocument second = _parser.Parse(" user@example.com \n山田 太郎\nUSER@example.com",
            TestContext.Current.CancellationToken);

        Assert.Equal(first.ContentSha256, second.ContentSha256);
    }

    [Fact]
    public void Parse_表示名付きの行は山括弧内のメールアドレスだけを使用する()
    {
        MemberListDocument document = _parser.Parse(
            "山田 太郎 <taro@example.com>\n鈴木 花子 <hanako@example.com>\ntaro@example.com",
            TestContext.Current.CancellationToken);

        Assert.Equal(["taro@example.com", "hanako@example.com"], document.Addresses);
    }

    [Theory]
    [InlineData("山田 太郎 <taro@example.com")]
    [InlineData("山田 太郎 taro@example.com>")]
    [InlineData("山田 太郎 <>")]
    public void Parse_不正な表示名付き形式を拒否する(string text)
    {
        Assert.Throws<InvalidDataException>(() => _parser.Parse(text, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public void Parse_有効な入力がなければ失敗する(string text)
    {
        Assert.Throws<InvalidDataException>(() => _parser.Parse(text, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("user@example.com\t別の列")]
    [InlineData("user@example.com\0")]
    public void Parse_複数列または制御文字を拒否する(string text)
    {
        Assert.Throws<InvalidDataException>(() => _parser.Parse(text, TestContext.Current.CancellationToken));
    }

    // ReadOnlySpan<char>.EnumerateLines()はフォームフィード(U+000C)・NEL(U+0085)も行区切りとして
    // 扱ってしまい、行分割にそれを使うと制御文字が行内容として現れず検証をすり抜けてしまう
    // 回帰を防ぐためのテスト。(char)キャストで生成し、ソースファイルへ生の制御文字を含めない。
    [Theory]
    [InlineData(0x000C)]
    [InlineData(0x0085)]
    public void Parse_フォームフィードやNELなどの制御文字も拒否する(int codePoint)
    {
        string text = "user@example.com" + (char)codePoint + "other@example.com";

        Assert.Throws<InvalidDataException>(() => _parser.Parse(text, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Parse_長すぎる行を拒否する()
    {
        string text = new('a', MemberTextParser.MaximumLineLength + 1);

        Assert.Throws<InvalidDataException>(() => _parser.Parse(text, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Parse_上限を超える件数を拒否する()
    {
        string text = string.Join('\n',
            Enumerable.Range(0, MemberTextParser.MaximumEntries + 1)
                .Select(index => $"user{index}@example.com"));

        Assert.Throws<InvalidDataException>(() => _parser.Parse(text, TestContext.Current.CancellationToken));
    }
}