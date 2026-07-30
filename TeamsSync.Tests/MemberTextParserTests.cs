using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Tests;

public sealed class MemberTextParserTests
{
    private readonly MemberTextParser _parser = new();

    [Fact]
    public void Parse_改行と空行を正規化し重複を除去する()
    {
        var document = _parser.Parse(
            " user@example.com\r\n\r\nUSER@example.com\n山田　太郎\r佐藤 花子 ");

        Assert.Equal(
            ["user@example.com", "山田　太郎", "佐藤 花子"],
            document.Addresses);
        Assert.Equal("貼り付け入力.txt", document.FileName);
        Assert.Equal(64, document.ContentSha256.Length);
    }

    [Fact]
    public void Parse_正規化後の内容が同じなら同じハッシュを返す()
    {
        var first = _parser.Parse("user@example.com\r\n山田 太郎");
        var second = _parser.Parse(" user@example.com \n山田 太郎\nUSER@example.com");

        Assert.Equal(first.ContentSha256, second.ContentSha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public void Parse_有効な入力がなければ失敗する(string text)
    {
        Assert.Throws<InvalidDataException>(() => _parser.Parse(text));
    }

    [Theory]
    [InlineData("user@example.com\t別の列")]
    [InlineData("user@example.com\0")]
    public void Parse_複数列または制御文字を拒否する(string text)
    {
        Assert.Throws<InvalidDataException>(() => _parser.Parse(text));
    }

    [Fact]
    public void Parse_長すぎる行を拒否する()
    {
        var text = new string('a', MemberTextParser.MaximumLineLength + 1);

        Assert.Throws<InvalidDataException>(() => _parser.Parse(text));
    }

    [Fact]
    public void Parse_上限を超える件数を拒否する()
    {
        var text = string.Join('\n',
            Enumerable.Range(0, MemberTextParser.MaximumEntries + 1)
                .Select(index => $"user{index}@example.com"));

        Assert.Throws<InvalidDataException>(() => _parser.Parse(text));
    }
}