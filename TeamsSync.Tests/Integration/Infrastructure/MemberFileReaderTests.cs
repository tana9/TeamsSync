using System.Security.Cryptography;
using System.Text;

using ClosedXML.Excel;

using TeamsSync.Application.Models;
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
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void ReadCsv_RecognizesHeaderQuotedValuesAndDuplicates()
    {
        string path = Path.Combine(_directory, "members.csv");
        File.WriteAllText(path,
            "email,name\n\"User1@example.com\",\"User, One\"\nuser1@example.com,duplicate\nuser2@example.com,Two\n");
        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);
        Assert.Equal(["User1@example.com", "user2@example.com"], result.Addresses);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            result.ContentSha256);
    }

    [Theory]
    [InlineData("email,name\n\"user@example.com,User\n", 2)]
    [InlineData("email,name\nuser\"@example.com,User\n", 2)]
    [InlineData("email,name\n\"user@example.com\"x,User\n", 2)]
    public void ReadCsv_引用符崩れを行番号付きで拒否する(string content, int expectedRow)
    {
        string path = Path.Combine(_directory, "bad-quotes.csv");
        File.WriteAllText(path, content);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new MemberListReader().Read(path, CancellationToken.None));

        Assert.Contains($"{expectedRow}行目", exception.Message);
        Assert.Contains("CSV形式", exception.Message);
    }

    [Theory]
    [InlineData("email,name\nuser@example.com\n", 1)]
    [InlineData("email,name\nuser@example.com,User,extra\n", 3)]
    public void ReadCsv_ヘッダーと異なる列数を行番号付きで拒否する(string content, int actualColumns)
    {
        string path = Path.Combine(_directory, "column-mismatch.csv");
        File.WriteAllText(path, content);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new MemberListReader().Read(path, CancellationToken.None));

        Assert.Contains("2行目", exception.Message);
        Assert.Contains("1行目: 2列", exception.Message);
        Assert.Contains($"2行目: {actualColumns}列", exception.Message);
    }

    [Fact]
    public void ReadCsv_引用符内改行を含む正常なデータは読み込める()
    {
        string path = Path.Combine(_directory, "quoted-newline.csv");
        File.WriteAllText(path, "email,name\n\"user@example.com\",\"User\nOne\"\n");

        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user@example.com"], result.Addresses);
    }

    [Fact]
    public void ReadCsv_氏名列よりメールアドレス列を優先する()
    {
        string path = Path.Combine(_directory, "preferred-email.csv");
        File.WriteAllText(path, "氏名,メールアドレス\n山田 太郎,taro@example.com\n");

        MemberListDocument document = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal("メールアドレス", document.DetectedColumn);
        Assert.Equal(["taro@example.com"], document.Addresses);
        Assert.False(document.IsNameColumn);
    }

    [Fact]
    public void ReadExcel_氏名列よりUPN列を優先する()
    {
        string path = Path.Combine(_directory, "preferred-upn.xlsx");
        using (XLWorkbook book = new())
        {
            IXLWorksheet sheet = book.AddWorksheet("Members");
            sheet.Cell(1, 1).Value = "氏名";
            sheet.Cell(1, 2).Value = "UPN";
            sheet.Cell(2, 1).Value = "山田 太郎";
            sheet.Cell(2, 2).Value = "taro@example.com";
            book.SaveAs(path);
        }

        MemberListDocument document = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal("UPN", document.DetectedColumn);
        Assert.Equal(["taro@example.com"], document.Addresses);
        Assert.False(document.IsNameColumn);
    }

    [Fact]
    public void ReadCsv_メールアドレスが空の行は氏名列で補う()
    {
        string path = Path.Combine(_directory, "mixed.csv");
        File.WriteAllText(path,
            "氏名,メールアドレス\n山田 太郎,\n佐藤 花子,sato@example.com\n鈴木 一郎,\n");

        MemberListDocument document = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["山田 太郎", "sato@example.com", "鈴木 一郎"], document.Addresses);
        Assert.True(document.IsNameColumn);
        Assert.Equal("メールアドレス / 氏名", document.DetectedColumn);
    }

    [Fact]
    public void ReadExcel_UPNが空の行は氏名列で補う()
    {
        string path = Path.Combine(_directory, "mixed.xlsx");
        using (XLWorkbook book = new())
        {
            IXLWorksheet sheet = book.AddWorksheet("Members");
            sheet.Cell(1, 1).Value = "氏名";
            sheet.Cell(1, 2).Value = "UserPrincipalName";
            sheet.Cell(2, 1).Value = "山田 太郎";
            sheet.Cell(2, 2).Value = "";
            sheet.Cell(3, 1).Value = "佐藤 花子";
            sheet.Cell(3, 2).Value = "sato@example.com";
            book.SaveAs(path);
        }

        MemberListDocument document = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["山田 太郎", "sato@example.com"], document.Addresses);
        Assert.True(document.IsNameColumn);
    }

    [Fact]
    public void ReadCsv_全行にメールアドレスがあれば氏名列が存在してもフォールバック扱いにしない()
    {
        string path = Path.Combine(_directory, "email-complete.csv");
        File.WriteAllText(path,
            "氏名,メールアドレス\n山田 太郎,taro@example.com\n佐藤 花子,sato@example.com\n");

        MemberListDocument document = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["taro@example.com", "sato@example.com"], document.Addresses);
        Assert.False(document.IsNameColumn);
        Assert.Equal("メールアドレス", document.DetectedColumn);
    }

    [Fact]
    public void ReadExcel_単一列しか検出できない場合は列数不一致を行番号付きで拒否する()
    {
        // メールアドレス列・氏名列のどちらか一方しか検出できない場合はフォールバック先がなく、
        // 列が少ない行を対象列の値なしとして無警告で除外してしまうため、従来通り拒否する
        string path = Path.Combine(_directory, "column-mismatch.xlsx");
        using (XLWorkbook book = new())
        {
            IXLWorksheet sheet = book.AddWorksheet("Members");
            sheet.Cell(1, 1).Value = "email";
            sheet.Cell(1, 2).Value = "社員番号";
            sheet.Cell(2, 1).Value = "user1@example.com";
            sheet.Cell(2, 2).Value = "1001";
            sheet.Cell(3, 1).Value = "user2@example.com";
            // 3行目は社員番号列を意図的に空のままにし、LastCellUsedが1列目までしかない行を作る
            book.SaveAs(path);
        }

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new MemberListReader().Read(path, CancellationToken.None));

        Assert.Contains("3行目", exception.Message);
        Assert.Contains("1行目: 2列", exception.Message);
        Assert.Contains("3行目: 1列", exception.Message);
    }

    [Fact]
    public void ReadExcel_メールアドレス氏名両方の列がある場合は末尾セル空欄の行を許容する()
    {
        // メールアドレス列・氏名列の両方を検出できる場合、Excelで末尾セルが未入力のため
        // LastCellUsedが縮んだ行(=氏名列側だけ空欄)は、氏名列でフォールバックできるため
        // エラーにせず読み込めることを確認する
        string path = Path.Combine(_directory, "trailing-blank.xlsx");
        using (XLWorkbook book = new())
        {
            IXLWorksheet sheet = book.AddWorksheet("Members");
            sheet.Cell(1, 1).Value = "email";
            sheet.Cell(1, 2).Value = "name";
            sheet.Cell(2, 1).Value = "user1@example.com";
            sheet.Cell(2, 2).Value = "User One";
            sheet.Cell(3, 1).Value = "user2@example.com";
            // 3行目はname列を意図的に空のままにし、LastCellUsedが1列目までしかない行を作る
            book.SaveAs(path);
        }

        MemberListDocument document = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user1@example.com", "user2@example.com"], document.Addresses);
    }

    [Fact]
    public void ReadExcel_メールアドレス氏名両方の列があってもヘッダーより列が多い行は拒否する()
    {
        // 末尾セル空欄による列不足は許容するが、ヘッダーより列が多い行は誤って別データが
        // 紛れ込んだ可能性が高いため、従来通り拒否されることを確認する
        string path = Path.Combine(_directory, "extra-column.xlsx");
        using (XLWorkbook book = new())
        {
            IXLWorksheet sheet = book.AddWorksheet("Members");
            sheet.Cell(1, 1).Value = "email";
            sheet.Cell(1, 2).Value = "name";
            sheet.Cell(2, 1).Value = "user1@example.com";
            sheet.Cell(2, 2).Value = "User One";
            sheet.Cell(3, 1).Value = "user2@example.com";
            sheet.Cell(3, 2).Value = "User Two";
            sheet.Cell(3, 3).Value = "予期しない列";
            book.SaveAs(path);
        }

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new MemberListReader().Read(path, CancellationToken.None));

        Assert.Contains("3行目", exception.Message);
        Assert.Contains("1行目: 2列", exception.Message);
        Assert.Contains("3行目: 3列", exception.Message);
    }

    [Fact]
    public void ReadCsv_BOM付きUTF16はStreamReaderの自動判定で読み込める()
    {
        // CsvEncodingDetectorはUTF-8/UTF-8 BOM付き/Shift-JISしか判定せずUTF-16はShift-JISの
        // Encodingを返すが、StreamReaderのdetectEncodingFromByteOrderMarks(既定true)がBOMを見て
        // 実際のデコードでは指定したEncodingより優先されるため、BOM付きファイルは結果的に読める
        string path = Path.Combine(_directory, "utf16-bom.csv");
        File.WriteAllText(path, "メールアドレス,氏名\nuser@example.com,山田太郎\n", Encoding.Unicode);

        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user@example.com"], result.Addresses);
        Assert.Equal("メールアドレス", result.DetectedColumn);
    }

    [Fact]
    public void ReadCsv_BOMなしUTF16は文字コードエラーになる()
    {
        // BOMがない場合はStreamReaderの自動判定が働かないため、Shift-JISとして誤って
        // デコードされ、無効なバイト列として文字コードエラーになる
        string path = Path.Combine(_directory, "utf16-nobom.csv");
        File.WriteAllBytes(path, Encoding.Unicode.GetBytes("メールアドレス,氏名\nuser@example.com,山田太郎\n"));

        InvalidDataException ex =
            Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
        Assert.Contains("文字コード", ex.Message);
    }

    [Fact]
    public void ReadCsv_DetectsUtf8WithBom()
    {
        string path = Path.Combine(_directory, "utf8-bom.csv");
        File.WriteAllText(path, "メールアドレス,氏名\nuser@example.com,山田太郎\n", new UTF8Encoding(true));

        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user@example.com"], result.Addresses);
        Assert.Equal("メールアドレス", result.DetectedColumn);
    }

    [Fact]
    public void ReadCsv_DetectsWindows31J()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string path = Path.Combine(_directory, "shift-jis.csv");
        File.WriteAllText(path, "メールアドレス,氏名\nuser@example.com,髙橋太郎\n", Encoding.GetEncoding(932));

        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user@example.com"], result.Addresses);
        Assert.Equal("メールアドレス", result.DetectedColumn);
    }

    [Fact]
    public void ReadCsv_RecognizesJapaneseNameHeader()
    {
        string path = Path.Combine(_directory, "names.csv");
        File.WriteAllText(path, "氏名\n山田 太郎\n佐藤　花子\n", new UTF8Encoding(false));

        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["山田 太郎", "佐藤　花子"], result.Addresses);
        Assert.Equal("氏名", result.DetectedColumn);
        Assert.True(result.IsNameColumn);
    }

    [Fact]
    public void ReadCsv_ヘッダーが認識できない場合はIsNameColumnがfalse()
    {
        string path = Path.Combine(_directory, "unrecognized-header.csv");
        File.WriteAllText(path, "識別子\nuser1@example.com\n");

        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal("1列目（ヘッダーなし）", result.DetectedColumn);
        Assert.False(result.IsNameColumn);
    }

    [Fact]
    public void ReadExcel_ReadsFirstWorksheetAndRecognizesJapaneseHeader()
    {
        string path = Path.Combine(_directory, "members.xlsx");
        using (XLWorkbook workbook = new())
        {
            IXLWorksheet sheet = workbook.AddWorksheet("メンバー");
            sheet.Cell(1, 1).Value = "メールアドレス";
            sheet.Cell(2, 1).Value = "user1@example.com";
            sheet.Cell(3, 1).Value = "user2@example.com";
            workbook.SaveAs(path);
        }

        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);
        Assert.Equal(["user1@example.com", "user2@example.com"], result.Addresses);
        Assert.Equal("メンバー", result.SourceName);
        Assert.Equal("メールアドレス", result.DetectedColumn);
    }

    [Fact]
    public void Read_RejectsEmptyList()
    {
        string path = Path.Combine(_directory, "empty.csv");
        File.WriteAllText(path, "email\n");
        Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
    }

    [Fact]
    public void Read_RejectsUnsupportedExtension()
    {
        string path = Path.Combine(_directory, "members.xls");
        File.WriteAllText(path, "");
        Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
    }

    [Fact]
    public void Read_ファイルサイズが上限を超えると例外()
    {
        string path = Path.Combine(_directory, "huge.csv");
        // 内容の妥当性を問わず、サイズ判定はFileInfo.Lengthだけで行われるためダミーバイト列で十分
        File.WriteAllBytes(path, new byte[MemberFileSecurityValidator.MaximumFileSizeBytes + 1]);

        InvalidDataException ex =
            Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));
        Assert.Contains("サイズ", ex.Message);
    }

    [Fact]
    public void Read_他プロセスが排他的に開いているファイルは分かりやすいメッセージで例外()
    {
        string path = Path.Combine(_directory, "locked.csv");
        File.WriteAllText(path, "email\nuser1@example.com\n");
        using FileStream lockStream = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        InvalidDataException ex =
            Assert.Throws<InvalidDataException>(() => new MemberListReader().Read(path, CancellationToken.None));

        Assert.Contains("他のプログラム", ex.Message);
    }

    [Fact]
    public void Read_他プロセスが読み取り共有のみで開いているファイルは読み込める()
    {
        // Excelなどが書込みアクセスを保持したまま読み取り共有(FileShare.Read)は許可しているケースを模擬する。
        // MemberListReaderがFileShare.ReadWriteで開くことで、完全排他でない限り読み込めることを確認する
        string path = Path.Combine(_directory, "shared-read.csv");
        File.WriteAllText(path, "email\nuser1@example.com\n");
        using FileStream otherProcess = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        MemberListDocument result = new MemberListReader().Read(path, CancellationToken.None);

        Assert.Equal(["user1@example.com"], result.Addresses);
    }

    [Fact]
    public void Read_キャンセル済みトークンでは例外()
    {
        string path = Path.Combine(_directory, "cancel.csv");
        File.WriteAllText(path, "email\nuser1@example.com\nuser2@example.com\n");
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new MemberListReader().Read(path, cts.Token));
    }
}