using TeamsSync.Domain.Teams;

namespace TeamsSync.Tests.Unit.Domain;

public sealed class MemberIdentifierLineTests
{
    [Fact]
    public void FormatDisplayLine_表示名とメールアドレスを山括弧形式へ組み立てる()
    {
        string line = MemberIdentifierLine.FormatDisplayLine("山田 太郎", "taro@example.com");

        Assert.Equal("山田 太郎 <taro@example.com>", line);
    }

    [Fact]
    public void FormatDisplayLine_表示名が空ならメールアドレスのみを返す()
    {
        string line = MemberIdentifierLine.FormatDisplayLine("", "taro@example.com");

        Assert.Equal("taro@example.com", line);
    }

    [Fact]
    public void FormatDisplayLine_表示名の制御文字と山括弧は空白へ置き換える()
    {
        string line = MemberIdentifierLine.FormatDisplayLine("Weird<Name>\tHere", "weird@example.com");

        Assert.Equal("Weird Name  Here <weird@example.com>", line);
    }

    [Fact]
    public void FormatDisplayLine_組み立てた行はParseDisplayLineで同じメールアドレスへ復元できる()
    {
        string line = MemberIdentifierLine.FormatDisplayLine("山田 太郎", "taro@example.com");

        DisplayLineFormat format = MemberIdentifierLine.ParseDisplayLine(line, out string identifier);

        Assert.Equal(DisplayLineFormat.BracketedIdentifier, format);
        Assert.Equal("taro@example.com", identifier);
    }

    [Fact]
    public void ParseDisplayLine_山括弧を含まない行はそのまま識別子として返す()
    {
        DisplayLineFormat format = MemberIdentifierLine.ParseDisplayLine("taro@example.com", out string identifier);

        Assert.Equal(DisplayLineFormat.PlainIdentifier, format);
        Assert.Equal("taro@example.com", identifier);
    }

    [Fact]
    public void ParseDisplayLine_空行はそのままPlainIdentifierとして空文字を返す()
    {
        DisplayLineFormat format = MemberIdentifierLine.ParseDisplayLine("   ", out string identifier);

        Assert.Equal(DisplayLineFormat.PlainIdentifier, format);
        Assert.Equal("", identifier);
    }

    [Theory]
    [InlineData("山田 太郎 <taro@example.com")]
    [InlineData("山田 太郎 taro@example.com>")]
    [InlineData("山田 太郎 <taro@example.com> extra")]
    public void ParseDisplayLine_山括弧の対応が崩れている場合はMalformedBrackets(string line)
    {
        DisplayLineFormat format = MemberIdentifierLine.ParseDisplayLine(line, out string identifier);

        Assert.Equal(DisplayLineFormat.MalformedBrackets, format);
        Assert.Equal("", identifier);
    }

    [Fact]
    public void ParseDisplayLine_山括弧内が空の場合はEmptyBracketedIdentifier()
    {
        DisplayLineFormat format = MemberIdentifierLine.ParseDisplayLine("山田 太郎 <>", out string identifier);

        Assert.Equal(DisplayLineFormat.EmptyBracketedIdentifier, format);
        Assert.Equal("", identifier);
    }
}