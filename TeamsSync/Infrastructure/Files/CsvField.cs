namespace TeamsSync.Infrastructure.Files;

// SyncResultWriter(監査ログ)とMemberListCsvExporter(メンバー一覧CSV出力)が同一のCSVエスケープ処理を
// 個別に実装していたため、ここへ集約する
/// <summary>値をCSVの1フィールドとして安全な形式へ変換する</summary>
internal static class CsvField
{
    /// <summary>値をダブルクォートで囲み、内部のダブルクォートをエスケープする</summary>
    public static string Escape(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
