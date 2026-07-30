using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

public sealed class MemberListReader : IMemberListReader
{
    // 想定外に大きい／壊れたファイルを早期に拒否するための上限。
    // ファイルサイズはテキスト貼り付け(MemberTextParser.MaximumTextLength=500,000文字)より大きくてよいが、
    // 無制限だとFile.ReadAllBytes等で容易にメモリを枯渇させられるため常識的な値に制限する。
    public const long MaximumFileSizeBytes = 10 * 1024 * 1024;

    // 行数はテキスト貼り付けの上限(MemberTextParser.MaximumEntries)と揃え、入力経路によらず一貫した上限にする。
    public const int MaximumRows = 5000;

    // 列数は通常のメンバー名簿では数列で収まるため、数百列を超える場合は誤ったファイルの可能性が高いとみなす。
    public const int MaximumColumns = 200;
    public const long MaximumExpandedArchiveBytes = 100 * 1024 * 1024;

    private static readonly string[] HeaderNames =
        ["email", "mail", "upn", "userprincipalname", "name", "displayname", "メール", "メールアドレス", "氏名", "姓名", "名前"];
    private static readonly string[] AddressHeaderNames =
        ["email", "mail", "upn", "userprincipalname", "メール", "メールアドレス"];

    // Excelなどが排他的にファイルを開いている場合のWin32エラーコード(ERROR_SHARING_VIOLATION)に対応するHResult。
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    public MemberListDocument Read(string path, CancellationToken cancellationToken)
    {
        try
        {
            // File.ReadAllBytesで全体を読み込む前に、FileInfo.Lengthだけでサイズ超過を判定して即座に拒否する
            var info = new FileInfo(path);
            if (info.Length > MaximumFileSizeBytes)
                throw new InvalidDataException(
                    $"ファイルサイズは{MaximumFileSizeBytes / 1024 / 1024:N0}MBまでです（{info.Length / 1024 / 1024.0:N1}MB）。");

            var initialHash = ComputeSha256(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var (values, source, column) = extension switch
            {
                ".csv" => ReadCsv(path, cancellationToken),
                ".xlsx" => ReadExcel(path, cancellationToken),
                _ => throw new InvalidDataException("対応形式は .csv と .xlsx です。")
            };
            cancellationToken.ThrowIfCancellationRequested();
            var addresses = values.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (addresses.Count == 0)
                throw new InvalidDataException("メンバーのメールアドレスがありません。");
            var finalHash = ComputeSha256(path);
            if (!string.Equals(initialHash, finalHash, StringComparison.Ordinal))
                throw new InvalidDataException("読込中にファイルが変更されました。もう一度選択してください。");
            return new MemberListDocument(addresses, info.Name, info.FullName, info.LastWriteTime,
                source, column, finalHash);
        }
        catch (IOException ex) when (ex.HResult == SharingViolationHResult)
        {
            throw new InvalidDataException(
                "ファイルが他のプログラム（Excelなど）で開かれているため読み込めません。閉じてから再度お試しください。", ex);
        }
    }

    private static (IEnumerable<string>, string, string) ReadCsv(string path, CancellationToken cancellationToken)
    {
        var encoding = DetectCsvEncoding(path);
        using var stream = OpenShared(path);
        using var parser = new TextFieldParser(stream, encoding, true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.ReadFields() ?? [];
            if (fields.Length > MaximumColumns)
                throw new InvalidDataException($"CSVの{rows.Count + 1}行目の列数が{MaximumColumns:N0}列を超えています。");
            rows.Add(fields);
            // 全行をListへ溜め込む前に件数超過を検知し、巨大CSVを最後まで読み切ってからメモリを使い切ることを防ぐ
            if (rows.Count > MaximumRows)
                throw new InvalidDataException($"CSVの行数は{MaximumRows:N0}行までです。");
        }

        var extracted = ExtractColumn(rows);
        return (extracted.Values, "CSV", extracted.Column);
    }

    private static Encoding DetectCsvEncoding(string path)
    {
        // BOMの判定は先頭数バイトだけで足りるため、File.ReadAllBytesでファイル全体を読み込まない
        Span<byte> preamble = stackalloc byte[4];
        int read;
        using (var stream = OpenShared(path))
        {
            read = stream.Read(preamble);
        }

        var header = preamble[..read];
        if (header.StartsWith(Encoding.UTF8.Preamble))
            return new UTF8Encoding(true, true);
        if (header.StartsWith(Encoding.UTF32.Preamble))
            return new UTF32Encoding(false, true, true);
        if (header.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
            return new UTF32Encoding(true, true, true);
        if (header.StartsWith(Encoding.Unicode.Preamble))
            return new UnicodeEncoding(false, true, true);
        if (header.StartsWith(Encoding.BigEndianUnicode.Preamble))
            return new UnicodeEncoding(true, true, true);

        // BOMがない場合はUTF-8として妥当かをStreamReaderでチャンク単位に検証する。
        // byte[]とstringの両方を全体分保持しないため、ReadAllBytes+GetStringよりピークメモリが小さい。
        try
        {
            using var stream = OpenShared(path);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
            var buffer = new char[8192];
            while (reader.Read(buffer, 0, buffer.Length) > 0)
            {
            }

            return new UTF8Encoding(false, true);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
    }

    private static (IEnumerable<string>, string, string) ReadExcel(string path, CancellationToken cancellationToken)
    {
        ValidateExcelArchive(path, cancellationToken);
        using var stream = OpenShared(path);
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("Excelにワークシートがありません。");

        // RowsUsed()で全使用セルを展開する前に使用範囲の行数・列数を確認し、
        // 極端に大きい／横長なシートを実際の展開処理に入る前に拒否する
        var rowCount = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var columnCount = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (rowCount > MaximumRows)
            throw new InvalidDataException($"Excelの行数は{MaximumRows:N0}行までです（{rowCount:N0}行）。");
        if (columnCount > MaximumColumns)
            throw new InvalidDataException($"Excelの列数は{MaximumColumns:N0}列までです（{columnCount:N0}列）。");

        var rows = new List<string[]>();
        foreach (var row in sheet.RowsUsed())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = Math.Max(1, row.LastCellUsed()?.Address.ColumnNumber ?? 1);
            rows.Add(row.Cells(1, width).Select(c => c.GetFormattedString()).ToArray());
        }

        var extracted = ExtractColumn(rows);
        return (extracted.Values, sheet.Name, extracted.Column);
    }

    private static void ValidateExcelArchive(string path, CancellationToken cancellationToken)
    {
        using var stream = OpenShared(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Length > MaximumExpandedArchiveBytes - expandedBytes)
                throw new InvalidDataException(
                    $"Excelの展開後サイズは{MaximumExpandedArchiveBytes / 1024 / 1024:N0}MBまでです。");
            expandedBytes += entry.Length;
        }
    }

    private static (IEnumerable<string> Values, string Column) ExtractColumn(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return ([], "1列目");
        var index = FindPreferredColumn(rows[0]);
        var hasHeader = index >= 0;
        var column = hasHeader ? index : 0;
        var label = hasHeader ? rows[0][column].Trim() : "1列目（ヘッダーなし）";
        return (rows.Skip(hasHeader ? 1 : 0).Where(r => r.Length > column).Select(r => r[column]), label);
    }

    private static int FindPreferredColumn(IReadOnlyList<string> headers)
    {
        var addressIndex = headers.Select(NormalizeHeader).ToList().FindIndex(
            header => AddressHeaderNames.Contains(header, StringComparer.OrdinalIgnoreCase));
        return addressIndex >= 0
            ? addressIndex
            : headers.Select(NormalizeHeader).ToList().FindIndex(
                header => HeaderNames.Contains(header, StringComparer.OrdinalIgnoreCase));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = OpenShared(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // Excelなどが書込み用に開いたまま読み取り共有は許可しているケースを読めるようにするため、
    // File.OpenRead既定のFileShare.ReadWriteへ緩め、他プロセスの読み書きを妨げないようにする。
    // 完全排他(FileShare.None)のロックはこれでも読めないため、読込前後のSHA256比較(Read参照)で
    // 途中変更を検知して安全側に倒す。
    private static FileStream OpenShared(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    private static string NormalizeHeader(string value)
    {
        return value.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant();
    }
}
