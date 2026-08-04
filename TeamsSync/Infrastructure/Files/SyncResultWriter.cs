using System.Text;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Logging;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     同期プランと実行結果を、日時と対象チーム名をファイル名としたCSVログとして自動的に記録する。
///     CSVインジェクション対策として外部由来の値は数式として解釈されないよう無害化する
/// </summary>
/// <param name="logDirectory">
///     ログの出力先フォルダー。省略時はユーザー別のLocalApplicationData配下を使う。
///     テストから一時フォルダーを指定できるようにコンストラクター引数にしている
/// </param>
public sealed class SyncResultWriter(string? logDirectory = null, TimeProvider? timeProvider = null,
    IIdentifierGenerator? identifierGenerator = null) : ISyncResultWriter
{
    // OWASPが推奨するCSVインジェクション対策として、数式の開始として解釈され得る文字
    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@'];

    private readonly string _logDirectory = logDirectory ?? AuditLogging.LogDirectory;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IIdentifierGenerator _identifierGenerator = identifierGenerator ?? new IdentifierGenerator();

    /// <inheritdoc />
    public void VerifyWriteAccess()
    {
        Directory.CreateDirectory(_logDirectory);
        string probePath = Path.Combine(_logDirectory, $".write-test-{_identifierGenerator.NewGuid():N}.tmp");
        using FileStream _ = new(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
            FileOptions.DeleteOnClose);
    }

    /// <summary>
    ///     同期実行結果を、チーム名・モード・操作種別・アドレス・成否・エラーの列を持つCSVとして
    ///     実行日時・実行ID・対象チーム名を含む一意なファイル名でログフォルダーへ書き出す
    /// </summary>
    public string WriteAutoLog(SyncPlan plan, SyncOperationsResult result, Guid executionId)
    {
        Directory.CreateDirectory(_logDirectory);
        string stem = $"{_timeProvider.GetLocalNow():yyyyMMdd_HHmmss_fff}_{executionId:N}_{SanitizeFileName(plan.Team.DisplayName)}";
        string path = CreateUniquePath(stem);
        AtomicFileWriter.Write(path, stream => WriteCsv(stream, plan, result), false, _identifierGenerator);
        return path;
    }

    private static void WriteCsv(Stream stream, SyncPlan plan, SyncOperationsResult result)
    {
        using StreamWriter writer = new(stream, new UTF8Encoding(true), leaveOpen: true);
        writer.WriteLine("チーム,同期モード,操作,表示名,メールアドレス,結果,エラー");
        IReadOnlyList<SyncChange> plannedOperations = plan.Operations;
        if (plannedOperations.Count == 0 && result.Operations.Count > 0)
        {
            // 実際の同期(SyncExecutionCoordinator.ExecuteAsync)ではresult.OperationsはSyncExecutorが
            // plan.Operationsを1件ずつ実行して積み上げるため、この分岐は本番経路では到達しない。
            // プラン全体を組み立てずに実行結果の出力だけを検証したいテストのための簡易経路として残している
            foreach (SyncOperationResult item in result.Operations)
            {
                WriteRow(writer, plan, item.Kind, item.DisplayName, item.Email,
                    StatusLabel(item), item.Error ?? "");
            }
        }
        else
        {
            for (int index = 0; index < plannedOperations.Count; index++)
            {
                SyncChange planned = plannedOperations[index];
                SyncOperationResult? executed = index < result.Operations.Count ? result.Operations[index] : null;
                WriteRow(writer, plan, planned.Kind, planned.DisplayName, planned.Email,
                    executed is null ? "未実行" : StatusLabel(executed),
                    executed is null
                        ? result.Cancelled ? "同期がキャンセルされたため未実行" : "実行結果が記録されていません"
                        : executed.Error ?? "");
            }
        }

        writer.Flush();
    }

    /// <summary>操作結果を利用者向けの日本語ラベルへ変換する。状態不明は失敗と区別して「不明」と表示する</summary>
    private static string StatusLabel(SyncOperationResult item)
    {
        return item.Uncertain ? "不明" : item.Succeeded ? "成功" : "失敗";
    }

    /// <summary>同期操作1件をCSVへ出力する</summary>
    private static void WriteRow(TextWriter writer, SyncPlan plan, ChangeKind kind, string displayName,
        string email, string status, string error)
    {
        writer.WriteLine(string.Join(",", CsvExternal(plan.Team.DisplayName), Csv(SyncModeLabel(plan.Mode)),
            Csv(OperationLabel(kind)), CsvExternal(displayName), CsvExternal(email), Csv(status), CsvExternal(error)));
    }

    /// <summary>同期モードを利用者向けの日本語へ変換する</summary>
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

    /// <summary>実行した操作を利用者向けの日本語へ変換する</summary>
    private static string OperationLabel(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Add => "追加",
            ChangeKind.Remove => "削除",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "実行結果に未対応の操作種別が含まれています。")
        };
    }

    /// <summary>既存ファイルを上書きせず、衝突時は連番を付けた別名で新規作成する</summary>
    private string CreateUniquePath(string stem)
    {
        for (int suffix = 1; ; suffix++)
        {
            string fileName = suffix == 1 ? $"{stem}.csv" : $"{stem}_{suffix}.csv";
            string path = Path.Combine(_logDirectory, fileName);
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    // タイムスタンプ・実行ID(GUID)・拡張子・CreateUniquePathの連番接尾辞と合わせても
    // 255文字のファイル名コンポーネント上限に余裕を持って収まる長さ
    private const int MaximumSanitizedFileNameLength = 100;

    /// <summary>ファイル名として使えない文字を"_"へ置き換え、極端に長いチーム名は切り詰める</summary>
    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Concat(value.Select(c => invalidChars.Contains(c) ? '_' : c)).Trim();
        if (sanitized.Length > MaximumSanitizedFileNameLength)
        {
            sanitized = sanitized[..MaximumSanitizedFileNameLength].TrimEnd();
        }

        return sanitized.Length == 0 ? "team" : sanitized;
    }

    /// <summary>値をダブルクォートで囲み、内部のダブルクォートをエスケープしてCSVフィールド化する</summary>
    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    // テナントやGraph API由来の外部値をCSV出力する際に使う。
    // 数式注入(CSVインジェクション)を防ぐため、無害化してからCSVエスケープする
    /// <summary>CSVインジェクション対策の無害化を行ったうえでCSVフィールド化する</summary>
    private static string CsvExternal(string value)
    {
        return Csv(SanitizeFormulaInjection(value));
    }

    /// <summary>
    ///     値の先頭が数式トリガー文字の場合、表計算ソフトに数式評価されないようシングルクォートを付与する
    /// </summary>
    private static string SanitizeFormulaInjection(string value)
    {
        // 先頭の空白・タブ・改行を除いた最初の文字が=, +, -, @の場合、
        // 表計算ソフトが数式として評価してしまう。先頭にシングルクォートを付与し、
        // 文字列として扱わせる(OWASP CSV Injection Prevention Cheat Sheet準拠)
        string trimmed = value.TrimStart();
        return trimmed.Length > 0 && Array.IndexOf(FormulaTriggerChars, trimmed[0]) >= 0 ? "'" + value : value;
    }
}