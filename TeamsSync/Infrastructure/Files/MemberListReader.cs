using System.Globalization;
using System.Text;

using ClosedXML.Excel;

using CsvHelper;
using CsvHelper.Configuration;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     CSV(.csv)またはExcel(.xlsx)のメンバーリストファイルを読み込み、アドレス一覧へ変換する。
///     サイズ・行数・列数上限やZip展開後サイズなどの安全性検証は<see cref="MemberFileSecurityValidator" />へ、
///     文字コード判定は<see cref="CsvEncodingDetector" />へ委譲する
/// </summary>
public sealed class MemberListReader : IMemberListReader
{
    // Excelなどが排他的にファイルを開いている場合のWin32エラーコード(ERROR_SHARING_VIOLATION)に対応するHResult
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    private static readonly string[] HeaderNames =
        ["email", "mail", "upn", "userprincipalname", "name", "displayname", "メール", "メールアドレス", "氏名", "姓名", "名前"];

    private static readonly string[] AddressHeaderNames =
        ["email", "mail", "upn", "userprincipalname", "メール", "メールアドレス"];

    // ヘッダー行も1行目のデータとして自前で扱う(ExtractColumn参照)ためHasHeaderRecord=false。
    // 行数上限の判定を物理行単位で行うため空行もスキップせずIgnoreBlankLines=false。
    // 引用符崩れなどの不正データは、誤った列を同期対象として採用しないよう行番号付きで拒否する。
    // 引用符内の改行は正規のCSVとして許可するためLineBreakInQuotedFieldIsBadDataはfalseのままにする
    private static readonly CsvConfiguration CsvReaderConfiguration = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
        IgnoreBlankLines = false,
        ExceptionMessagesContainRawData = false,
        BadDataFound = args => throw new InvalidDataException(
            $"{Math.Max(1, args.Context.Parser?.Row ?? 1)}行目のCSV形式が正しくありません。引用符の対応を確認してください。")
    };

    /// <summary>
    ///     指定したパスのファイルを拡張子に応じてCSV/Excelとして読み込み、アドレス列を抽出する。
    ///     読込前後でファイル内容のハッシュを比較し、読込中の変更を検知する
    /// </summary>
    public MemberListDocument Read(string path, CancellationToken cancellationToken)
    {
        try
        {
            // File.ReadAllBytesで全体を読み込む前に、FileInfo.Lengthだけでサイズ超過を判定して即座に拒否する
            FileInfo info = new(path);
            MemberFileSecurityValidator.EnsureFileSizeWithinLimit(info.Length);

            string initialHash = ComputeSha256(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            (IEnumerable<string> values, string source, string column, bool isNameColumn) = extension switch
            {
                ".csv" => ReadCsv(path, cancellationToken),
                ".xlsx" => ReadExcel(path, cancellationToken),
                _ => throw new InvalidDataException("対応形式は .csv と .xlsx です。")
            };
            cancellationToken.ThrowIfCancellationRequested();
            List<string> addresses =
            [
                .. values.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
            if (addresses.Count == 0)
            {
                throw new InvalidDataException("メンバーのメールアドレスがありません。");
            }

            string finalHash = ComputeSha256(path);
            if (!string.Equals(initialHash, finalHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException("読込中にファイルが変更されました。もう一度選択してください。");
            }

            return new MemberListDocument(addresses, info.Name, info.FullName, info.LastWriteTime,
                source, column, finalHash, isNameColumn);
        }
        catch (IOException ex) when (ex.HResult == SharingViolationHResult)
        {
            throw new InvalidDataException(
                "ファイルが他のプログラム（Excelなど）で開かれているため読み込めません。閉じてから再度お試しください。", ex);
        }
    }

    /// <summary>CSVファイルを解析し、アドレス候補列を抽出する</summary>
    private static (IEnumerable<string>, string, string, bool) ReadCsv(string path,
        CancellationToken cancellationToken)
    {
        Encoding encoding = CsvEncodingDetector.Detect(path);
        using FileStream stream = SharedFileAccess.Open(path);
        using StreamReader reader = new(stream, encoding);
        using CsvReader csv = new(reader, CsvReaderConfiguration);
        List<string[]> rows = [];
        try
        {
            while (csv.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] fields = csv.Parser.Record ?? [];
                int physicalRow = csv.Parser.RawRow;
                MemberFileSecurityValidator.EnsureCsvColumnCountWithinLimit(fields.Length, physicalRow);
                if (rows.Count > 0 && fields.Length != rows[0].Length)
                {
                    throw new InvalidDataException(
                        $"{physicalRow}行目の列数が1行目と一致しません（1行目: {rows[0].Length}列、{physicalRow}行目: {fields.Length}列）。");
                }

                rows.Add(fields);
                // 全行をListへ溜め込む前に件数超過を検知し、巨大CSVを最後まで読み切ってからメモリを使い切ることを防ぐ
                MemberFileSecurityValidator.EnsureCsvRowCountWithinLimit(rows.Count);
            }
        }
        catch (CsvHelperException ex)
        {
            int physicalRow = Math.Max(1, csv.Parser.RawRow);
            throw new InvalidDataException($"{physicalRow}行目のCSV形式が正しくありません。引用符と列数を確認してください。", ex);
        }

        (IEnumerable<string> Values, string Column, bool IsNameColumn) extracted = ExtractColumn(rows);
        return (extracted.Values, "CSV", extracted.Column, extracted.IsNameColumn);
    }

    /// <summary>Excelファイルの先頭ワークシートを解析し、アドレス候補列を抽出する</summary>
    private static (IEnumerable<string>, string, string, bool) ReadExcel(string path,
        CancellationToken cancellationToken)
    {
        using (FileStream archiveStream = SharedFileAccess.Open(path))
        {
            MemberFileSecurityValidator.ValidateExcelArchive(archiveStream, cancellationToken);
        }

        using FileStream stream = SharedFileAccess.Open(path);
        using XLWorkbook book = new(stream);
        IXLWorksheet sheet = book.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("Excelにワークシートがありません。");

        // RowsUsed()で全使用セルを展開する前に使用範囲の行数・列数を確認し、
        // 極端に大きい／横長なシートを実際の展開処理に入る前に拒否する
        int rowCount = sheet.LastRowUsed()?.RowNumber() ?? 0;
        int columnCount = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        MemberFileSecurityValidator.EnsureExcelRowCountWithinLimit(rowCount);
        MemberFileSecurityValidator.EnsureExcelColumnCountWithinLimit(columnCount);

        List<string[]> rows = [];
        foreach (IXLRow row in sheet.RowsUsed())
        {
            cancellationToken.ThrowIfCancellationRequested();
            int width = Math.Max(1, row.LastCellUsed()?.Address.ColumnNumber ?? 1);
            rows.Add(row.Cells(1, width).Select(c => c.GetFormattedString()).ToArray());
        }

        (IEnumerable<string> Values, string Column, bool IsNameColumn) extracted = ExtractColumn(rows);
        return (extracted.Values, sheet.Name, extracted.Column, extracted.IsNameColumn);
    }

    /// <summary>
    ///     ヘッダー行からアドレス列(またはフォールバックの氏名列)を推定し、その列の値を抽出する
    /// </summary>
    private static (IEnumerable<string> Values, string Column, bool IsNameColumn) ExtractColumn(
        IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
        {
            return ([], "1列目", false);
        }

        (int index, bool isNameColumn) = FindPreferredColumn(rows[0]);
        bool hasHeader = index >= 0;
        int column = hasHeader ? index : 0;
        string label = hasHeader ? rows[0][column].Trim() : "1列目（ヘッダーなし）";
        return (rows.Skip(hasHeader ? 1 : 0).Where(r => r.Length > column).Select(r => r[column]), label,
            hasHeader && isNameColumn);
    }

    /// <summary>
    ///     ヘッダー名からメールアドレス列を優先的に探し、なければ氏名列を含む候補列を探す。
    ///     戻り値には、見つかった列がメールアドレス列ではなく氏名列かどうかもあわせて含める
    /// </summary>
    private static (int Index, bool IsNameColumn) FindPreferredColumn(IReadOnlyList<string> headers)
    {
        List<string> normalized = headers.Select(NormalizeHeader).ToList();
        int addressIndex = normalized.FindIndex(header =>
            AddressHeaderNames.Contains(header, StringComparer.OrdinalIgnoreCase));
        if (addressIndex >= 0)
        {
            return (addressIndex, false);
        }

        int fallbackIndex =
            normalized.FindIndex(header => HeaderNames.Contains(header, StringComparer.OrdinalIgnoreCase));
        return (fallbackIndex, fallbackIndex >= 0);
    }

    /// <summary>ファイル内容のSHA-256ハッシュを16進文字列で計算する</summary>
    private static string ComputeSha256(string path)
    {
        using FileStream stream = SharedFileAccess.Open(path);
        return MemberFileSecurityValidator.ComputeSha256(stream);
    }

    /// <summary>ヘッダー名の表記揺れ(前後空白・アンダースコア・空白・大文字小文字)を吸収する</summary>
    private static string NormalizeHeader(string value)
    {
        return value.Trim().Replace("_", "").Replace(" ", "").ToLowerInvariant();
    }
}
