using System.Text;

using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     同期プランと実行結果を、日時と対象チーム名をファイル名としたCSVログとして自動的に記録する。
///     CSVインジェクション対策として外部由来の値は数式として解釈されないよう無害化する。
/// </summary>
/// <param name="logDirectory">
///     ログの出力先フォルダー。省略時はEXEと同じフォルダー内の"logs"フォルダーを使う。
///     テストから一時フォルダーを指定できるようにコンストラクター引数にしている。
/// </param>
public sealed class SyncResultWriter(string? logDirectory = null) : ISyncResultWriter
{
    // OWASPが推奨するCSVインジェクション対策として、数式の開始として解釈され得る文字。
    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@'];
    private readonly string _logDirectory = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>
    ///     同期実行結果を、チーム名・モード・操作種別・アドレス・成否・エラーの列を持つCSVとして
    ///     "{実行日時}_{対象チーム名}.csv"のファイル名でログフォルダーへ書き出す。
    /// </summary>
    public void WriteAutoLog(SyncPlan plan, SyncExecutionResult result)
    {
        Directory.CreateDirectory(_logDirectory);
        string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeFileName(plan.Team.DisplayName)}.csv";
        string path = Path.Combine(_logDirectory, fileName);
        using StreamWriter writer = new(path, false, new UTF8Encoding(true));
        writer.WriteLine("team,mode,operation,email,result,error");
        foreach (SyncOperationResult item in result.Operations)
        {
            writer.WriteLine(string.Join(",", CsvExternal(plan.Team.DisplayName), Csv(plan.Mode.ToString()),
                Csv(item.Kind.ToString()),
                CsvExternal(item.Email), item.Succeeded ? "成功" : "失敗", CsvExternal(item.Error ?? "")));
        }
    }

    /// <summary>ファイル名として使えない文字を"_"へ置き換える。</summary>
    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Concat(value.Select(c => invalidChars.Contains(c) ? '_' : c)).Trim();
        return sanitized.Length == 0 ? "team" : sanitized;
    }

    /// <summary>値をダブルクォートで囲み、内部のダブルクォートをエスケープしてCSVフィールド化する。</summary>
    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    // テナントやGraph API由来の外部値をCSV出力する際に使う。
    // 数式注入(CSVインジェクション)を防ぐため、無害化してからCSVエスケープする。
    /// <summary>CSVインジェクション対策の無害化を行ったうえでCSVフィールド化する。</summary>
    private static string CsvExternal(string value)
    {
        return Csv(SanitizeFormulaInjection(value));
    }

    /// <summary>
    ///     値の先頭が数式トリガー文字の場合、表計算ソフトに数式評価されないようシングルクォートを付与する。
    /// </summary>
    private static string SanitizeFormulaInjection(string value)
    {
        // 先頭の空白・タブ・改行を除いた最初の文字が=, +, -, @の場合、
        // 表計算ソフトが数式として評価してしまう。先頭にシングルクォートを付与し、
        // 文字列として扱わせる(OWASP CSV Injection Prevention Cheat Sheet準拠)。
        string trimmed = value.TrimStart();
        return trimmed.Length > 0 && Array.IndexOf(FormulaTriggerChars, trimmed[0]) >= 0 ? "'" + value : value;
    }
}