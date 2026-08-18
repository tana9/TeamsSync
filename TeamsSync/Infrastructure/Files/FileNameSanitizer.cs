namespace TeamsSync.Infrastructure.Files;

// SyncResultWriter(監査ログ)とMemberListCsvExporter(メンバー一覧CSV出力)の両方が、
// ファイル名として使えない文字の置換と長さの切り詰めを個別に実装しており、
// 前者にだけ長さの切り詰めがある食い違いが生じていたため、ここへ集約する
/// <summary>文字列をファイル名として安全な形式(禁止文字の置換・長さの切り詰め)へ変換する</summary>
internal static class FileNameSanitizer
{
    // タイムスタンプ・実行ID(GUID)・拡張子・連番接尾辞などを組み合わせても
    // 255文字のファイル名コンポーネント上限に余裕を持って収まる長さ
    private const int MaximumLength = 100;

    /// <summary>
    ///     ファイル名として使えない文字を"_"へ置き換え、極端に長い値は切り詰める。
    ///     結果が空文字になる場合は<paramref name="fallback" />を返す
    /// </summary>
    public static string Sanitize(string value, string fallback)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Concat(value.Select(c => invalidChars.Contains(c) ? '_' : c)).Trim();
        if (sanitized.Length > MaximumLength)
        {
            sanitized = sanitized[..MaximumLength].TrimEnd();
        }

        return sanitized.Length == 0 ? fallback : sanitized;
    }
}
