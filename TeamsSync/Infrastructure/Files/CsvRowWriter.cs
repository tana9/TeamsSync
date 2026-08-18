using CsvHelper;

namespace TeamsSync.Infrastructure.Files;

internal static class CsvRowWriter
{
    /// <summary>フィールド一覧を指定した引用符指定で書き込み、次の行へ進める</summary>
    public static void WriteRow(CsvWriter csv, IEnumerable<string> fields, bool quoted)
    {
        foreach (string field in fields)
        {
            csv.WriteField(field, quoted);
        }

        csv.NextRecord();
    }
}
