using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using ClosedXML.Excel;
using TeamsSync.Infrastructure.Files;

namespace TeamsSync.Tests.Integration.Infrastructure;

public sealed class MemberFileReaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TeamsSync.Tests", Guid.NewGuid().ToString("N"));

    public MemberFileReaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void ReadExcel_展開後サイズが上限を超えるZipを拒否する()
    {
        var path = Path.Combine(_directory, "oversized.xlsx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var stream = archive.CreateEntry("xl/sharedStrings.xml", CompressionLevel.SmallestSize).Open())
        {
            var block = new byte[1024 * 1024];
            for (var i = 0; i <= MemberListReader.MaximumExpandedArchiveBytes / block.Length; i++)
                stream.Write(block);
        }

        var exception = Assert.Throws<InvalidDataException>(() =>
            new MemberListReader().Read(path, CancellationToken.None));

        Assert.Contains("展開後サイズ", exception.Message);
    }

    [Fact]
    public void ReadCsv_RecognizesHeaderQuotedValuesAndDuplicates()
    {
        var path = Path.Combine(_directory, "members.csv");
        File.WriteAllText(path,
            "email,name\n\"User1@example.com\",\"User, One\"\nuser1@example.com,duplicate\nuser2@example.com,Two\n");
        var result = new MemberListReader().Read(path, CancellationToken.None);
        Assert.Equal(["User1@example.com", "user2@example.com"], result.Addresses);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            result.ContentSha256);
    }

    [Fact]
    public void ReadCsv_氏名列よりメールアドレス列を優先する()
    {
        var path = Path.Combine(_directory, "preferred-email.csv");
        File.WriteAllText(path, "氏名,メールアドレス\n山田 太郎,taro@example.com\n");

        var document = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal("メールアドレス", document.DetectedColumn);
        Assert.Equal(["taro@example.com"], document.Addresses);
    }

    [Fact]
    public void ReadExcel_氏名列よりUPN列を優先する()
    {
        var path = Path.Combine(_directory, "preferred-upn.xlsx");
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Members");
            sheet.Cell(1, 1).Value = "氏名";
            sheet.Cell(1, 2).Value = "UPN";
            sheet.Cell(2, 1).Value = "山田 太郎";
            sheet.Cell(2, 2).Value = "taro@example.com";
            book.SaveAs(path);
        }

        var document = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal("UPN", document.DetectedColumn);
        Assert.Equal(["taro@example.com"], document.Addresses);
    }

    [Fact]
    public void ReadCsv_DetectsUtf8WithBom()
    {
        var path = Path.Combine(_directory, "utf8-bom.csv");
        File.WriteAllText(path, "メールアドレス,氏名\nuser@example.com,山田太郎\n", new UTF8Encoding(true));

        var result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user@example.com"], result.Addresses);
        Assert.Equal("メールアドレス", result.DetectedColumn);
    }

    [Fact]
    public void ReadCsv_DetectsUtf16LittleEndian()
    {
        var path = Path.Combine(_directory, "utf16.csv");
        File.WriteAllText(path, "メールアドレス,氏名\nuser@example.com,山田太郎\n", Encoding.Unicode);

        var result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user@example.com"], result.Addresses);
        Assert.Equal("メールアドレス", result.DetectedColumn);
    }

    [Fact]
    public void ReadCsv_DetectsWindows31J()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(_directory, "shift-jis.csv");
        File.WriteAllText(path, "メールアドレス,氏名\nuser@example.com,髙橋太郎\n", Encoding.GetEncoding(932));

        var result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user@example.com"], result.Addresses);
        Assert.Equal("メールアドレス", result.DetectedColumn);
    }

    [Fact]
    public void ReadCsv_RecognizesJapaneseNameHeader()
    {
        var path = Path.Combine(_directory, "names.csv");
        File.WriteAllText(path, "氏名\n山田 太郎\n佐藤　花子\n", new UTF8Encoding(false));

        var result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["山田 太郎", "佐藤　花子"], result.Addresses);
        Assert.Equal("氏名", result.DetectedColumn);
    }

    [Fact]
    public void ReadExcel_ReadsFirstWorksheetAndRecognizesJapaneseHeader()
    {
        var path = Path.Combine(_directory, "members.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("メンバー");
            sheet.Cell(1, 1).Value = "メールアドレス";
            sheet.Cell(2, 1).Value = "user1@example.com";
            sheet.Cell(3, 1).Value = "user2@example.com";
            workbook.SaveAs(path);
        }

        var result = new MemberListReader().Read(path, CancellationToken.None);
        Assert.Equal(["user1@example.com", "user2@example.com"], result.Addresses);
        Assert.Equal("メンバー", result.SourceName);
        Assert.Equal("メールアドレス", result.DetectedColumn);
    }

    [Fact]
    public void Read_RejectsEmptyList()
    {
        var path = Path.Combine(_directory, "empty.csv");
        File.WriteAllText(path, "email\n");
        Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
    }

    [Fact]
    public void Read_RejectsUnsupportedExtension()
    {
        var path = Path.Combine(_directory, "members.xls");
        File.WriteAllText(path, "");
        Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
    }

    [Fact]
    public void ReadCsv_行数が上限ちょうどなら成功する()
    {
        var path = Path.Combine(_directory, "rows-at-limit.csv");
        // ヘッダー1行 + データ(MaximumRows-1)行 = 合計MaximumRows行で上限ちょうど
        var lines = Enumerable.Range(0, MemberListReader.MaximumRows - 1).Select(i => $"user{i}@example.com");
        File.WriteAllText(path, "email\n" + string.Join('\n', lines) + "\n");

        var result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(MemberListReader.MaximumRows - 1, result.Addresses.Count);
    }

    [Fact]
    public void ReadCsv_行数が上限を超えると例外()
    {
        var path = Path.Combine(_directory, "rows-over-limit.csv");
        // ヘッダー1行 + データMaximumRows行 = 合計MaximumRows+1行で上限超過
        var lines = Enumerable.Range(0, MemberListReader.MaximumRows).Select(i => $"user{i}@example.com");
        File.WriteAllText(path, "email\n" + string.Join('\n', lines) + "\n");

        var ex = Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
        Assert.Contains("行数", ex.Message);
    }

    [Fact]
    public void ReadCsv_列数が上限を超えると例外()
    {
        var path = Path.Combine(_directory, "columns-over-limit.csv");
        var wideRow = string.Join(',', Enumerable.Range(0, MemberListReader.MaximumColumns + 1).Select(i => $"v{i}"));
        File.WriteAllText(path, wideRow + "\n");

        var ex = Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
        Assert.Contains("列数", ex.Message);
    }

    [Fact]
    public void Read_ファイルサイズが上限を超えると例外()
    {
        var path = Path.Combine(_directory, "huge.csv");
        // 内容の妥当性を問わず、サイズ判定はFileInfo.Lengthだけで行われるためダミーバイト列で十分
        File.WriteAllBytes(path, new byte[MemberListReader.MaximumFileSizeBytes + 1]);

        var ex = Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
        Assert.Contains("サイズ", ex.Message);
    }

    [Fact]
    public void ReadExcel_行数が上限を超えると例外()
    {
        var path = Path.Combine(_directory, "rows-over-limit.xlsx");
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Members");
            sheet.Cell(1, 1).Value = "email";
            for (var row = 0; row < MemberListReader.MaximumRows; row++)
                sheet.Cell(row + 2, 1).Value = $"user{row}@example.com";
            book.SaveAs(path);
        }

        var ex = Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
        Assert.Contains("行数", ex.Message);
    }

    [Fact]
    public void ReadExcel_極端に横長な場合は例外()
    {
        var path = Path.Combine(_directory, "wide.xlsx");
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Members");
            for (var column = 1; column <= MemberListReader.MaximumColumns + 1; column++)
                sheet.Cell(1, column).Value = $"col{column}";
            book.SaveAs(path);
        }

        var ex = Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
        Assert.Contains("列数", ex.Message);
    }

    [Fact]
    public void Read_他プロセスが排他的に開いているファイルは分かりやすいメッセージで例外()
    {
        var path = Path.Combine(_directory, "locked.csv");
        File.WriteAllText(path, "email\nuser1@example.com\n");
        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var ex = Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));

        Assert.Contains("他のプログラム", ex.Message);
    }

    [Fact]
    public void Read_他プロセスが読み取り共有のみで開いているファイルは読み込める()
    {
        // Excelなどが書込みアクセスを保持したまま読み取り共有(FileShare.Read)は許可しているケースを模擬する。
        // MemberListReaderがFileShare.ReadWriteで開くことで、完全排他でない限り読み込めることを確認する。
        var path = Path.Combine(_directory, "shared-read.csv");
        File.WriteAllText(path, "email\nuser1@example.com\n");
        using var otherProcess = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        var result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user1@example.com"], result.Addresses);
    }

    [Fact]
    public void Read_キャンセル済みトークンでは例外()
    {
        var path = Path.Combine(_directory, "cancel.csv");
        File.WriteAllText(path, "email\nuser1@example.com\nuser2@example.com\n");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new MemberListReader().Read(path, cts.Token));
    }
}
