using System.IO.Compression;
using System.Security.Cryptography;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     メンバーリストファイルの読込に関するセキュリティ・堅牢性検証(サイズ・行数・列数の上限、
///     Excel(.xlsx)のZip展開後サイズ、内容ハッシュ)をまとめて担当する。
/// </summary>
internal static class MemberFileSecurityValidator
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
    public const int MaximumArchiveEntries = 1000;
    public const long MaximumArchiveEntryBytes = 50 * 1024 * 1024;
    public const int MaximumCompressionRatio = 1000;

    /// <summary>ファイルサイズが上限内であることを検証する。</summary>
    public static void EnsureFileSizeWithinLimit(long lengthBytes)
    {
        if (lengthBytes > MaximumFileSizeBytes)
        {
            throw new InvalidDataException(
                $"ファイルサイズは{MaximumFileSizeBytes / 1024 / 1024:N0}MBまでです（{lengthBytes / 1024 / 1024.0:N1}MB）。");
        }
    }

    /// <summary>CSVの行数が上限内であることを検証する。</summary>
    public static void EnsureCsvRowCountWithinLimit(int rowCount)
    {
        if (rowCount > MaximumRows)
        {
            throw new InvalidDataException($"CSVの行数は{MaximumRows:N0}行までです。");
        }
    }

    /// <summary>CSVの列数が上限内であることを検証する。</summary>
    public static void EnsureCsvColumnCountWithinLimit(int columnCount, int rowNumber)
    {
        if (columnCount > MaximumColumns)
        {
            throw new InvalidDataException($"CSVの{rowNumber}行目の列数が{MaximumColumns:N0}列を超えています。");
        }
    }

    /// <summary>Excelの行数が上限内であることを検証する。</summary>
    public static void EnsureExcelRowCountWithinLimit(int rowCount)
    {
        if (rowCount > MaximumRows)
        {
            throw new InvalidDataException($"Excelの行数は{MaximumRows:N0}行までです（{rowCount:N0}行）。");
        }
    }

    /// <summary>Excelの列数が上限内であることを検証する。</summary>
    public static void EnsureExcelColumnCountWithinLimit(int columnCount)
    {
        if (columnCount > MaximumColumns)
        {
            throw new InvalidDataException($"Excelの列数は{MaximumColumns:N0}列までです（{columnCount:N0}列）。");
        }
    }

    /// <summary>Excel(.xlsx)をZipアーカイブとして展開後サイズを検証し、Zip爆弾的な入力を拒否する。</summary>
    public static void ValidateExcelArchive(Stream stream, CancellationToken cancellationToken)
    {
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        List<ArchiveEntryMetadata> entries = new(archive.Entries.Count);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new ArchiveEntryMetadata(entry.Length, entry.CompressedLength));
        }

        ValidateArchiveMetadata(entries);
    }

    internal static void ValidateArchiveMetadata(IReadOnlyList<ArchiveEntryMetadata> entries)
    {
        if (entries.Count > MaximumArchiveEntries)
            throw new InvalidDataException($"ExcelのZIPエントリー数は{MaximumArchiveEntries:N0}件までです。");
        long expandedBytes = 0;
        foreach (ArchiveEntryMetadata entry in entries)
        {
            if (entry.ExpandedLength > MaximumArchiveEntryBytes)
                throw new InvalidDataException($"Excel内の単一ファイルは{MaximumArchiveEntryBytes / 1024 / 1024:N0}MBまでです。");
            if (entry.ExpandedLength > 0 && (entry.CompressedLength == 0 ||
                entry.ExpandedLength / (double)entry.CompressedLength > MaximumCompressionRatio))
                throw new InvalidDataException("Excel内に圧縮率が異常に高いファイルが含まれています。");
            if (entry.ExpandedLength > MaximumExpandedArchiveBytes - expandedBytes)
            {
                throw new InvalidDataException(
                    $"Excelの展開後サイズは{MaximumExpandedArchiveBytes / 1024 / 1024:N0}MBまでです。");
            }

            expandedBytes += entry.ExpandedLength;
        }
    }

    internal readonly record struct ArchiveEntryMetadata(long ExpandedLength, long CompressedLength);

    /// <summary>ファイル内容のSHA-256ハッシュを16進文字列で計算する。</summary>
    public static string ComputeSha256(Stream stream)
    {
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
