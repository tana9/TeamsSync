using System.Text;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
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

    /// <inheritdoc />
    public void VerifyWriteAccess()
    {
        Directory.CreateDirectory(_logDirectory);
        string probePath = Path.Combine(_logDirectory, $".write-test-{Guid.NewGuid():N}.tmp");
        using FileStream _ = new(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
            FileOptions.DeleteOnClose);
    }

    /// <summary>
    ///     同期実行結果を、チーム名・モード・操作種別・アドレス・成否・エラーの列を持つCSVとして
    ///     実行日時・実行ID・対象チーム名を含む一意なファイル名でログフォルダーへ書き出す。
    /// </summary>
    public string WriteAutoLog(SyncPlan plan, SyncExecutionResult result, Guid executionId)
    {
        Directory.CreateDirectory(_logDirectory);
        string stem = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{executionId:N}_{SanitizeFileName(plan.Team.DisplayName)}";
        string path = CreateUniquePath(stem);
        AtomicFileWriter.Write(path, stream => WriteCsv(stream, plan, result), false);
        return path;
    }

    private static void WriteCsv(Stream stream, SyncPlan plan, SyncExecutionResult result)
    {
        using StreamWriter writer = new(stream, new UTF8Encoding(true), leaveOpen: true);
        writer.WriteLine("チーム,同期モード,操作,表示名,メールアドレス,結果,エラー");
        IReadOnlyList<SyncChange> plannedOperations = plan.Operations;
        if (plannedOperations.Count == 0 && result.Operations.Count > 0)
        {
            // 古い呼び出し元や単体利用との互換性を保つ。通常の同期では必ずプラン側に操作が存在する。
            foreach (SyncOperationResult item in result.Operations)
            {
                WriteRow(writer, plan, item.Kind, item.DisplayName, item.Email,
                    item.Succeeded ? "成功" : "失敗", item.Error ?? "");
            }
        }
        else
        {
            for (int index = 0; index < plannedOperations.Count; index++)
            {
                SyncChange planned = plannedOperations[index];
                SyncOperationResult? executed = index < result.Operations.Count ? result.Operations[index] : null;
                WriteRow(writer, plan, planned.Kind, planned.DisplayName, planned.Email,
                    executed is null ? "未実行" : executed.Succeeded ? "成功" : "失敗",
                    executed is null
                        ? result.Cancelled ? "同期がキャンセルされたため未実行" : "実行結果が記録されていません"
                        : executed.Error ?? "");
            }
        }

        writer.Flush();
    }

    /// <summary>同期操作1件をCSVへ出力する。</summary>
    private static void WriteRow(TextWriter writer, SyncPlan plan, ChangeKind kind, string displayName,
        string email, string status, string error)
    {
        writer.WriteLine(string.Join(",", CsvExternal(plan.Team.DisplayName), Csv(SyncModeLabel(plan.Mode)),
            Csv(OperationLabel(kind)), CsvExternal(displayName), CsvExternal(email), Csv(status), CsvExternal(error)));
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
    private string CreateUniquePath(string stem)
    {
        for (int suffix = 1;; suffix++)
        {
            string fileName = suffix == 1 ? $"{stem}.csv" : $"{stem}_{suffix}.csv";
            string path = Path.Combine(_logDirectory, fileName);
            if (!File.Exists(path)) return path;
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
