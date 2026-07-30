using System.Text;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

public sealed class SyncResultWriter : ISyncResultWriter
{
    // OWASPが推奨するCSVインジェクション対策として、数式の開始として解釈され得る文字。
    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@'];

    public void WriteCsv(string path, SyncPlan plan, SyncExecutionResult result)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("team,mode,operation,email,result,error");
        foreach (var item in result.Operations)
            writer.WriteLine(string.Join(",", CsvExternal(plan.Team.DisplayName), Csv(plan.Mode.ToString()),
                Csv(item.Kind.ToString()),
                CsvExternal(item.Email), item.Succeeded ? "成功" : "失敗", CsvExternal(item.Error ?? "")));
    }

    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    // テナントやGraph API由来の外部値をCSV出力する際に使う。
    // 数式注入(CSVインジェクション)を防ぐため、無害化してからCSVエスケープする。
    private static string CsvExternal(string value)
    {
        return Csv(SanitizeFormulaInjection(value));
    }

    private static string SanitizeFormulaInjection(string value)
    {
        // 先頭の空白・タブ・改行を除いた最初の文字が=, +, -, @の場合、
        // 表計算ソフトが数式として評価してしまう。先頭にシングルクォートを付与し、
        // 文字列として扱わせる(OWASP CSV Injection Prevention Cheat Sheet準拠)。
        var trimmed = value.TrimStart();
        return trimmed.Length > 0 && Array.IndexOf(FormulaTriggerChars, trimmed[0]) >= 0 ? "'" + value : value;
    }
}