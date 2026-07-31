using System.Text;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
/// 同期プランと実行結果をCSVファイルとして書き出す。CSVインジェクション対策として
/// 外部由来の値は数式として解釈されないよう無害化する。
/// </summary>
public sealed class SyncResultWriter : ISyncResultWriter
{
    // OWASPが推奨するCSVインジェクション対策として、数式の開始として解釈され得る文字。
    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@'];

    /// <summary>
    /// 同期実行結果を、チーム名・モード・操作種別・アドレス・成否・エラーの列を持つCSVとして書き出す。
    /// </summary>
    public void WriteCsv(string path, SyncPlan plan, SyncExecutionResult result)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("team,mode,operation,email,result,error");
        foreach (var item in result.Operations)
            writer.WriteLine(string.Join(",", CsvExternal(plan.Team.DisplayName), Csv(plan.Mode.ToString()),
                Csv(item.Kind.ToString()),
                CsvExternal(item.Email), item.Succeeded ? "成功" : "失敗", CsvExternal(item.Error ?? "")));
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
    /// 値の先頭が数式トリガー文字の場合、表計算ソフトに数式評価されないようシングルクォートを付与する。
    /// </summary>
    private static string SanitizeFormulaInjection(string value)
    {
        // 先頭の空白・タブ・改行を除いた最初の文字が=, +, -, @の場合、
        // 表計算ソフトが数式として評価してしまう。先頭にシングルクォートを付与し、
        // 文字列として扱わせる(OWASP CSV Injection Prevention Cheat Sheet準拠)。
        var trimmed = value.TrimStart();
        return trimmed.Length > 0 && Array.IndexOf(FormulaTriggerChars, trimmed[0]) >= 0 ? "'" + value : value;
    }
}