using System.Security.Cryptography;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     メンバーリストファイルの読込に関するセキュリティ・堅牢性検証(サイズ・行数・列数の上限、内容ハッシュ)をまとめて担当する
/// </summary>
internal static class MemberFileSecurityValidator
{
    // 想定外に大きい／壊れたファイルを早期に拒否するための上限。
    // ファイルサイズはテキスト貼り付け(MemberTextParser.MaximumTextLength=500,000文字)より大きくてよいが、
    // 無制限だとFile.ReadAllBytes等で容易にメモリを枯渇させられるため常識的な値に制限する
    public const long MaximumFileSizeBytes = 10 * 1024 * 1024;

    // 行数はテキスト貼り付けの上限(MemberTextParser.MaximumEntries)と揃え、入力経路によらず一貫した上限にする
    public const int MaximumRows = 5000;

    // 列数は通常のメンバー名簿では数列で収まるため、数百列を超える場合は誤ったファイルの可能性が高いとみなす
    public const int MaximumColumns = 200;

    /// <summary>ファイルサイズが上限内であることを検証する</summary>
    public static void EnsureFileSizeWithinLimit(long lengthBytes)
    {
        if (lengthBytes > MaximumFileSizeBytes)
        {
            throw new InvalidDataException(
                $"ファイルサイズは{MaximumFileSizeBytes / 1024 / 1024:N0}MBまでです（{lengthBytes / 1024 / 1024.0:N1}MB）。");
        }
    }

    /// <summary>CSVの行数が上限内であることを検証する</summary>
    public static void EnsureCsvRowCountWithinLimit(int rowCount)
    {
        if (rowCount > MaximumRows)
        {
            throw new InvalidDataException($"CSVの行数は{MaximumRows:N0}行までです。");
        }
    }

    /// <summary>CSVの列数が上限内であることを検証する</summary>
    public static void EnsureCsvColumnCountWithinLimit(int columnCount, int rowNumber)
    {
        if (columnCount > MaximumColumns)
        {
            throw new InvalidDataException($"CSVの{rowNumber}行目の列数が{MaximumColumns:N0}列を超えています。");
        }
    }

    /// <summary>Excelの行数が上限内であることを検証する</summary>
    public static void EnsureExcelRowCountWithinLimit(int rowCount)
    {
        if (rowCount > MaximumRows)
        {
            throw new InvalidDataException($"Excelの行数は{MaximumRows:N0}行までです（{rowCount:N0}行）。");
        }
    }

    /// <summary>Excelの列数が上限内であることを検証する</summary>
    public static void EnsureExcelColumnCountWithinLimit(int columnCount)
    {
        if (columnCount > MaximumColumns)
        {
            throw new InvalidDataException($"Excelの列数は{MaximumColumns:N0}列までです（{columnCount:N0}列）。");
        }
    }

    /// <summary>ファイル内容のSHA-256ハッシュを16進文字列で計算する</summary>
    public static string ComputeSha256(Stream stream)
    {
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>バイト列のSHA-256ハッシュを16進文字列で計算する</summary>
    public static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}