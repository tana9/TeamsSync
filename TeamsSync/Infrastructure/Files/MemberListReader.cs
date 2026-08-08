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

    private static readonly string[] AddressHeaderNames =
        ["email", "mail", "upn", "userprincipalname", "メール", "メールアドレス"];

    private static readonly string[] NameHeaderNames =
        ["name", "displayname", "氏名", "姓名", "名前"];

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
            ParsedMemberSource parsed = extension switch
            {
                ".csv" => ReadCsv(path, cancellationToken),
                ".xlsx" => ReadExcel(path, cancellationToken),
                _ => throw new InvalidDataException("対応形式は .csv と .xlsx です。")
            };
            cancellationToken.ThrowIfCancellationRequested();
            List<string> addresses =
            [
                .. parsed.Values.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
            if (addresses.Count == 0)
            {
                throw new InvalidDataException("メンバーのメールアドレスがありません。");
            }

            string finalHash = EnsureNotModifiedDuringRead(path, initialHash);

            return new MemberListDocument(addresses, info.Name, info.FullName, info.LastWriteTime,
                parsed.SourceName, parsed.Column, finalHash, parsed.IsNameColumn);
        }
        catch (IOException ex) when (ex.HResult == SharingViolationHResult)
        {
            throw new InvalidDataException(
                "ファイルが他のプログラム（Excelなど）で開かれているため読み込めません。閉じてから再度お試しください。", ex);
        }
    }

    /// <summary>
    ///     読込前後のファイル内容ハッシュを比較し、読込中にファイルが変更されていないか検証する
    /// </summary>
    private static string EnsureNotModifiedDuringRead(string path, string initialHash)
    {
        string finalHash = ComputeSha256(path);
        if (!string.Equals(initialHash, finalHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("読込中にファイルが変更されました。もう一度選択してください。");
        }

        return finalHash;
    }

    /// <summary>CSVファイルを解析し、アドレス候補列を抽出する</summary>
    private static ParsedMemberSource ReadCsv(string path, CancellationToken cancellationToken)
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

        ExtractedColumn extracted = ExtractColumn(rows);
        return new ParsedMemberSource(extracted.Values, "CSV", extracted.Column, extracted.IsNameColumn);
    }

    /// <summary>Excelファイルの先頭ワークシートを解析し、アドレス候補列を抽出する</summary>
    private static ParsedMemberSource ReadExcel(string path, CancellationToken cancellationToken)
    {
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
        ColumnSelection? selection = null;
        foreach (IXLRow row in sheet.RowsUsed())
        {
            cancellationToken.ThrowIfCancellationRequested();
            int width = Math.Max(1, row.LastCellUsed()?.Address.ColumnNumber ?? 1);
            string[] fields = row.Cells(1, width).Select(c => c.GetFormattedString()).ToArray();
            if (rows.Count == 0)
            {
                selection = FindColumns(fields);
            }
            else
            {
                fields = selection!.NormalizeRowWidth(fields, rows[0].Length, row.RowNumber());
            }

            rows.Add(fields);
        }

        ExtractedColumn extracted = ExtractColumn(rows);
        return new ParsedMemberSource(extracted.Values, sheet.Name, extracted.Column, extracted.IsNameColumn);
    }

    /// <summary>
    ///     ヘッダー行からメールアドレス列・氏名列を推定し、値を抽出する。列の見つかり方(なし/片方/両方)に
    ///     応じた分岐は<see cref="ColumnSelection" />自身へ委ねており、ここでは行を1件ずつ渡すだけでよい
    /// </summary>
    private static ExtractedColumn ExtractColumn(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
        {
            return new ExtractedColumn([], "1列目", false);
        }

        ColumnSelection selection = FindColumns(rows[0]);
        if (selection.IsEmpty)
        {
            return new ExtractedColumn(rows.Select(r => r.Length > 0 ? r[0] : ""), "1列目（ヘッダーなし）", false);
        }

        List<string> values = [];
        bool usedNameFallback = false;
        foreach (string[] row in rows.Skip(1))
        {
            (string value, bool fellBackToName) = selection.SelectValue(row);
            values.Add(value);
            usedNameFallback |= fellBackToName;
        }

        string label = selection.BuildLabel(rows[0], usedNameFallback);
        return new ExtractedColumn(values, label, usedNameFallback || selection.IsSingleNameColumn);
    }

    /// <summary>ヘッダー名からメールアドレス列・氏名列のインデックスをそれぞれ独立に探す</summary>
    private static ColumnSelection FindColumns(IReadOnlyList<string> headers)
    {
        List<string> normalized = headers.Select(NormalizeHeader).ToList();
        int addressIndex = normalized.FindIndex(header =>
            AddressHeaderNames.Contains(header, StringComparer.OrdinalIgnoreCase));
        int nameIndex = normalized.FindIndex(header =>
            NameHeaderNames.Contains(header, StringComparer.OrdinalIgnoreCase));
        return new ColumnSelection(addressIndex >= 0 ? addressIndex : null, nameIndex >= 0 ? nameIndex : null);
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

    // 旧実装は(IEnumerable<string>, string, string, bool)という名前なしタプルを返しており、
    // 文字列2つ(入力元名・検出列ラベル)が隣り合っているため、ReadCsv/ReadExcel内で返す順序を
    // 間違えても型では検出できなかった。名前付きのrecordへ置き換えて取り違えを防ぐ
    /// <summary>ヘッダー行からの列抽出結果(値・列ラベル・氏名列かどうか)</summary>
    private sealed record ExtractedColumn(IEnumerable<string> Values, string Column, bool IsNameColumn);

    /// <summary>CSV/Excelから読み取った値と、入力元・検出列のラベルをまとめた結果</summary>
    private sealed record ParsedMemberSource(IEnumerable<string> Values, string SourceName, string Column,
        bool IsNameColumn);

    // 「見つかった列がなし/片方だけ/両方」という分岐、行ごとの値の選び方、列不足行の許容判定、
    // 列ラベルの組み立てをすべてここへ集約している。以前はこれらの分岐がExtractColumn・
    // ExtractWithFallback・NormalizeExcelRowWidthの3メソッドに分散しており、呼び出し側は
    // 単に「どの行がどの意味を持つか」を渡すだけでよくなった
    /// <summary>ヘッダー行から見つかったメールアドレス列・氏名列のインデックス(見つからない場合はnull)</summary>
    private sealed record ColumnSelection(int? AddressIndex, int? NameIndex)
    {
        /// <summary>メールアドレス列・氏名列のどちらも見つからなかったかどうか(ヘッダーなし扱い)</summary>
        public bool IsEmpty => AddressIndex is null && NameIndex is null;

        /// <summary>メールアドレス列・氏名列の両方が見つかっており、行ごとに互いを補えるかどうか</summary>
        public bool CanFallBackToName => AddressIndex is not null && NameIndex is not null;

        /// <summary>片方の列しか見つからず、それが氏名列であるかどうか(氏名列のみ検出のケース)</summary>
        public bool IsSingleNameColumn => AddressIndex is null && NameIndex is not null;

        /// <summary>
        ///     1行分のセル値から、この選択に従って値を1つ取り出す。両方の列が見つかっている場合は
        ///     メールアドレス列を優先し、その値が空欄のときだけ氏名列の値を返す。戻り値の
        ///     UsedNameFallbackは、実際に氏名列の値(空でないもの)を採用したかどうかを表す
        /// </summary>
        public (string Value, bool UsedNameFallback) SelectValue(string[] row)
        {
            string address = AddressIndex is { } addressIndex && row.Length > addressIndex
                ? row[addressIndex].Trim()
                : "";
            if (!string.IsNullOrWhiteSpace(address))
            {
                return (address, false);
            }

            string name = NameIndex is { } nameIndex && row.Length > nameIndex ? row[nameIndex] : "";
            return (name, CanFallBackToName && !string.IsNullOrWhiteSpace(name));
        }

        /// <summary>
        ///     ヘッダー行から、この選択に対応する列ラベルを組み立てる。氏名列へフォールバックした行が
        ///     1件もなければメールアドレス列(または唯一検出できた列)の見出しだけを返す
        /// </summary>
        public string BuildLabel(string[] header, bool usedNameFallback)
        {
            if (!CanFallBackToName)
            {
                int column = AddressIndex ?? NameIndex!.Value;
                return header.Length > column ? header[column].Trim() : "1列目";
            }

            string addressLabel = header.Length > AddressIndex!.Value ? header[AddressIndex.Value].Trim() : "";
            if (!usedNameFallback)
            {
                return addressLabel;
            }

            string nameLabel = header.Length > NameIndex!.Value ? header[NameIndex.Value].Trim() : "";
            return $"{addressLabel} / {nameLabel}";
        }

        /// <summary>
        ///     データ行の列数をヘッダー行と揃える。ヘッダーより多い場合は誤って別データが紛れ込んだ
        ///     可能性が高いため、CSVの列数不一致チェックと同様に行番号付きのエラーとする。少ない場合、
        ///     メールアドレス列・氏名列の両方が見つかっていれば(<see cref="CanFallBackToName" />)、
        ///     Excelで末尾セルが未入力のとき<c>LastCellUsed</c>がそこまで伸びず行の見かけの列数が
        ///     縮む挙動を吸収するため空欄で埋め、<see cref="SelectValue" />のフォールバック判定に委ねる。
        ///     片方の列しか見つからない場合はフォールバックできず値を無警告で取りこぼしてしまうため、
        ///     従来通りエラーとする
        /// </summary>
        public string[] NormalizeRowWidth(string[] fields, int headerWidth, int rowNumber)
        {
            if (fields.Length == headerWidth)
            {
                return fields;
            }

            if (fields.Length < headerWidth && CanFallBackToName)
            {
                return [.. fields, .. Enumerable.Repeat("", headerWidth - fields.Length)];
            }

            throw new InvalidDataException(
                $"{rowNumber}行目の列数が1行目と一致しません（1行目: {headerWidth}列、{rowNumber}行目: {fields.Length}列）。");
        }
    }
}