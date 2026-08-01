using System.Text;

using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     同期プランと実行結果を、日時と対象チーム名をファイル名としたCSVログとして自動的に記録する。
///     CSVインジェクション対策として外部由来の値は数式として解釈されないよう無害化する。
/// </summary>
/// <param name="logDirectory">
///     ログの出力先フォルダー。省略時はユーザー別のLocalApplicationData配下を使う。
///     テストから一時フォルダーを指定できるようにコンストラクター引数にしている。
/// </param>
public sealed class SyncResultWriter(string? logDirectory = null) : ISyncResultWriter
{
    // OWASPが推奨するCSVインジェクション対策として、数式の開始として解釈され得る文字。
    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@'];
    private readonly string _logDirectory = logDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TeamsSync", "Logs");

    /// <summary>
    ///     同期実行結果を、チーム名・モード・操作種別・アドレス・成否・エラーの列を持つCSVとして
    ///     実行日時・実行ID・対象チーム名を含む一意なファイル名でログフォルダーへ書き出す。
    /// </summary>
    public string WriteAutoLog(SyncPlan plan, SyncExecutionResult result, Guid executionId)
    {
        Directory.CreateDirectory(_logDirectory);
        string stem = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{executionId:N}_{SanitizeFileName(plan.Team.DisplayName)}";
        FileStream stream = CreateUniqueFile(stem, out string path);
        using StreamWriter writer = new(stream, new UTF8Encoding(true));
        writer.WriteLine("チーム,同期モード,操作,表示名,メールアドレス,結果,エラー");
        foreach (SyncOperationResult item in result.Operations)
        {
            writer.WriteLine(string.Join(",", CsvExternal(plan.Team.DisplayName), Csv(SyncModeLabel(plan.Mode)),
                Csv(OperationLabel(item.Kind)),
                CsvExternal(item.DisplayName), CsvExternal(item.Email), item.Succeeded ? "成功" : "失敗",
                CsvExternal(item.Error ?? "")));
        }

        return path;
    }

    /// <summary>同期モードを利用者向けの日本語へ変換する。</summary>
    private static string SyncModeLabel(SyncMode mode)
    {
        return mode switch
        {
            SyncMode.AddOnly => "追加のみ",
            SyncMode.RemoveSpecified => "指定メンバーを削除",
            SyncMode.FullSync => "完全同期",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未対応の同期モードです。")
        };
    }

    /// <summary>実行した操作を利用者向けの日本語へ変換する。</summary>
    private static string OperationLabel(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Add => "追加",
            ChangeKind.Remove => "削除",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "実行結果に未対応の操作種別が含まれています。")
        };
    }

    /// <summary>既存ファイルを上書きせず、衝突時は連番を付けた別名で新規作成する。</summary>
    private FileStream CreateUniqueFile(string stem, out string path)
    {
        for (int suffix = 1;; suffix++)
        {
            string fileName = suffix == 1 ? $"{stem}.csv" : $"{stem}_{suffix}.csv";
            path = Path.Combine(_logDirectory, fileName);
            try
            {
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(path))
            {
                // 同名ログが既にある場合だけ別名を試す。ディスク障害などのIOExceptionは呼び出し元へ返す。
            }
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
