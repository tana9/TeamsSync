using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
/// CSV(.csv)またはExcel(.xlsx)のメンバーリストファイルを読み込み、アドレス一覧へ変換する。
/// サイズ・行数・列数上限やZip展開後サイズなどの安全性検証は<see cref="MemberFileSecurityValidator"/>へ、
/// 文字コード判定は<see cref="CsvEncodingDetector"/>へ委譲する。
/// </summary>
public sealed class MemberListReader : IMemberListReader
{
    public const long MaximumFileSizeBytes = MemberFileSecurityValidator.MaximumFileSizeBytes;
    public const int MaximumRows = MemberFileSecurityValidator.MaximumRows;
    public const int MaximumColumns = MemberFileSecurityValidator.MaximumColumns;
    public const long MaximumExpandedArchiveBytes = MemberFileSecurityValidator.MaximumExpandedArchiveBytes;

    private static readonly string[] HeaderNames =
        ["email", "mail", "upn", "userprincipalname", "name", "displayname", "メール", "メールアドレス", "氏名", "姓名", "名前"];
    private static readonly string[] AddressHeaderNames =
        ["email", "mail", "upn", "userprincipalname", "メール", "メールアドレス"];

    // Excelなどが排他的にファイルを開いている場合のWin32エラーコード(ERROR_SHARING_VIOLATION)に対応するHResult。
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    /// <summary>
    /// 指定したパスのファイルを拡張子に応じてCSV/Excelとして読み込み、アドレス列を抽出する。
    /// 読込前後でファイル内容のハッシュを比較し、読込中の変更を検知する。
    /// </summary>
    public MemberListDocument Read(string path, CancellationToken cancellationToken)
    {
        try
        {
            // File.ReadAllBytesで全体を読み込む前に、FileInfo.Lengthだけでサイズ超過を判定して即座に拒否する
            var info = new FileInfo(path);
            MemberFileSecurityValidator.EnsureFileSizeWithinLimit(info.Length);

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

    // ヘッダー行も1行目のデータとして自前で扱う(ExtractColumn参照)ためHasHeaderRecord=false。
    // 行数上限の判定を物理行単位で行うため空行もスキップせずIgnoreBlankLines=false。
    // 引用符の対応崩れなど多少壊れたCSVでも読み進められるよう、BadDataFoundはnull(無視)にしている。
    private static readonly CsvConfiguration CsvReaderConfiguration = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
        IgnoreBlankLines = false,
        BadDataFound = null
    };

    /// <summary>CSVファイルを解析し、アドレス候補列を抽出する。</summary>
    private static (IEnumerable<string>, string, string) ReadCsv(string path, CancellationToken cancellationToken)
    {
        var encoding = CsvEncodingDetector.Detect(path);
        using var stream = OpenShared(path);
        using var reader = new StreamReader(stream, encoding);
        using var csv = new CsvReader(reader, CsvReaderConfiguration);
        List<string[]> rows = [];
        while (csv.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = csv.Parser.Record ?? [];
            MemberFileSecurityValidator.EnsureCsvColumnCountWithinLimit(fields.Length, rows.Count + 1);
            rows.Add(fields);
            // 全行をListへ溜め込む前に件数超過を検知し、巨大CSVを最後まで読み切ってからメモリを使い切ることを防ぐ
            MemberFileSecurityValidator.EnsureCsvRowCountWithinLimit(rows.Count);
        }

        var extracted = ExtractColumn(rows);
        return (extracted.Values, "CSV", extracted.Column);
    }

    /// <summary>Excelファイルの先頭ワークシートを解析し、アドレス候補列を抽出する。</summary>
    private static (IEnumerable<string>, string, string) ReadExcel(string path, CancellationToken cancellationToken)
    {
        using (var archiveStream = OpenShared(path))
            MemberFileSecurityValidator.ValidateExcelArchive(archiveStream, cancellationToken);
        using var stream = OpenShared(path);
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("Excelにワークシートがありません。");

        // RowsUsed()で全使用セルを展開する前に使用範囲の行数・列数を確認し、
        // 極端に大きい／横長なシートを実際の展開処理に入る前に拒否する
        var rowCount = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var columnCount = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        MemberFileSecurityValidator.EnsureExcelRowCountWithinLimit(rowCount);
        MemberFileSecurityValidator.EnsureExcelColumnCountWithinLimit(columnCount);

        List<string[]> rows = [];
        foreach (var row in sheet.RowsUsed())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = Math.Max(1, row.LastCellUsed()?.Address.ColumnNumber ?? 1);
            rows.Add(row.Cells(1, width).Select(c => c.GetFormattedString()).ToArray());
        }

        var extracted = ExtractColumn(rows);
        return (extracted.Values, sheet.Name, extracted.Column);
    }

    /// <summary>
    /// ヘッダー行からアドレス列(またはフォールバックの氏名列)を推定し、その列の値を抽出する。
    /// </summary>
    private static (IEnumerable<string> Values, string Column) ExtractColumn(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return ([], "1列目");
        var index = FindPreferredColumn(rows[0]);
        var hasHeader = index >= 0;
        var column = hasHeader ? index : 0;
        var label = hasHeader ? rows[0][column].Trim() : "1列目（ヘッダーなし）";
        return (rows.Skip(hasHeader ? 1 : 0).Where(r => r.Length > column).Select(r => r[column]), label);
    }

    /// <summary>
    /// ヘッダー名からメールアドレス列を優先的に探し、なければ氏名列を含む候補列を探す。
    /// </summary>
    private static int FindPreferredColumn(IReadOnlyList<string> headers)
    {
        var addressIndex = headers.Select(NormalizeHeader).ToList().FindIndex(
            header => AddressHeaderNames.Contains(header, StringComparer.OrdinalIgnoreCase));
        return addressIndex >= 0
            ? addressIndex
            : headers.Select(NormalizeHeader).ToList().FindIndex(
                header => HeaderNames.Contains(header, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>ファイル内容のSHA-256ハッシュを16進文字列で計算する。</summary>
    private static string ComputeSha256(string path)
    {
        using var stream = OpenShared(path);
        return MemberFileSecurityValidator.ComputeSha256(stream);
    }

    // Excelなどが書込み用に開いたまま読み取り共有は許可しているケースを読めるようにするため、
    // File.OpenRead既定のFileShare.ReadWriteへ緩め、他プロセスの読み書きを妨げないようにする。
    // 完全排他(FileShare.None)のロックはこれでも読めないため、読込前後のSHA256比較(Read参照)で
    // 途中変更を検知して安全側に倒す。
    /// <summary>他プロセスによる読み書きを妨げないよう共有モードでファイルを開く。</summary>
    private static FileStream OpenShared(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    /// <summary>ヘッダー名の表記揺れ(前後空白・アンダースコア・空白・大文字小文字)を吸収する。</summary>
    private static string NormalizeHeader(string value)
    {
        return value.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant();
    }
}
