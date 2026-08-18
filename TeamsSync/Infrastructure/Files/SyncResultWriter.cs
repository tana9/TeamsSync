using System.Globalization;
using System.Text;

using CsvHelper;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Logging;

namespace TeamsSync.Infrastructure.Files;

/// <summary>
///     同期プランと実行結果を、日時と対象チーム名をファイル名としたCSVログとして自動的に記録する
/// </summary>
/// <param name="logDirectory">
///     ログの出力先フォルダー。省略時はユーザー別のLocalApplicationData配下を使う。
///     テストから一時フォルダーを指定できるようにコンストラクター引数にしている
/// </param>
/// <param name="timeProvider">ログのファイル名・記録時刻に使う時刻源。省略時はシステム時刻を使う</param>
/// <param name="identifierGenerator">一時ファイル名の生成に使うID生成器。省略時は既定の実装を使う</param>
public sealed class SyncResultWriter(string? logDirectory = null, TimeProvider? timeProvider = null,
    IIdentifierGenerator? identifierGenerator = null) : ISyncResultWriter
{
    private readonly string _logDirectory = logDirectory ?? AuditLogging.SyncResultLogDirectory;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IIdentifierGenerator _identifierGenerator = identifierGenerator ?? new IdentifierGenerator();

    // ヘッダー行は既存の監査ログ形式に合わせ引用符なしで出力する(データ行だけ全フィールドを引用符で囲む)
    private static readonly string[] HeaderColumns =
    [
        "実行日時", "実行ID", "操作ユーザー表示名", "操作ユーザーオブジェクトID", "対象チーム", "同期モード",
        "表示名", "メールアドレス", "状態", "詳細", "結果", "エラー"
    ];

    /// <inheritdoc />
    public void VerifyWriteAccess()
    {
        Directory.CreateDirectory(_logDirectory);
        string probePath = Path.Combine(_logDirectory, $".write-test-{_identifierGenerator.NewGuid():N}.tmp");
        using FileStream _ = new(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
            FileOptions.DeleteOnClose);
    }

    /// <summary>
    ///     実行日時・実行ID・操作ユーザー・対象チーム・同期モードを各行の列として持つ、
    ///     単一のテーブルのチームメンバー変更一覧CSVとして、実行日時・実行ID・対象チーム名を
    ///     含む一意なファイル名でログフォルダーへ書き出す
    /// </summary>
    public string WriteAutoLog(SyncPlan plan, SyncOperationsResult result, SyncAuditContext auditContext)
    {
        Directory.CreateDirectory(_logDirectory);
        DateTimeOffset executedAt = _timeProvider.GetLocalNow();
        string sanitizedTeamName = FileNameSanitizer.Sanitize(plan.Team.DisplayName, "team");
        string stem = $"{executedAt:yyyyMMdd_HHmmss_fff}_{auditContext.ExecutionId:N}_{sanitizedTeamName}";
        string path = CreateUniquePath(stem);
        AtomicFileWriter.Write(path, stream => WriteCsv(stream, plan, result, auditContext, executedAt), false,
            _identifierGenerator);
        return path;
    }

    private static void WriteCsv(Stream stream, SyncPlan plan, SyncOperationsResult result,
        SyncAuditContext auditContext, DateTimeOffset executedAt)
    {
        using StreamWriter writer = new(stream, new UTF8Encoding(true), leaveOpen: true);
        using CsvWriter csv = new(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        CsvRowWriter.WriteRow(csv, HeaderColumns, quoted: false);

        string[] executionColumns =
        [
            FormatTimestamp(executedAt), auditContext.ExecutionId.ToString(), auditContext.ActorDisplayName ?? "",
            auditContext.ActorObjectId ?? "", plan.Team.DisplayName, SyncModeLabel(plan.Mode)
        ];

        Dictionary<SyncChange, SyncOperationResult> executedResults = new(ReferenceEqualityComparer.Instance);
        IReadOnlyList<SyncChange> operations = plan.Operations;
        for (int index = 0; index < operations.Count && index < result.Results.Count; index++)
        {
            executedResults[operations[index]] = result.Results[index];
        }

        foreach (SyncChange change in plan.Changes.OrderBy(DisplayOrder))
        {
            (string status, string error) = ResultColumns(change, executedResults, result);
            IEnumerable<string> row = executionColumns.Concat(
            [
                change.DisplayName, change.Email, ChangeKindText.Label(change.Kind),
                SyncChangeReasonText.Format(change.Reason), status, error
            ]);
            CsvRowWriter.WriteRow(csv, row, quoted: true);
        }

        csv.Flush();
    }

    /// <summary>変更1件の実行結果列(結果・エラー)を組み立てる。追加・削除以外は実行対象外のため空欄にする</summary>
    private static (string Status, string Error) ResultColumns(SyncChange change,
        IReadOnlyDictionary<SyncChange, SyncOperationResult> executedResults, SyncOperationsResult result)
    {
        if (!change.IsOperation)
        {
            return ("", "");
        }

        if (!executedResults.TryGetValue(change, out SyncOperationResult? executed))
        {
            return ("未実行",
                result.Cancelled ? "同期がキャンセルされたため未実行" : "実行結果が記録されていません");
        }

        return (StatusLabel(executed), executed.Error ?? "");
    }

    /// <summary>差分一覧画面と同じ並び順(エラー→削除→追加→所有者保護→その他)を返す</summary>
    private static int DisplayOrder(SyncChange change)
    {
        return change.Kind switch
        {
            ChangeKind.Error => 0,
            ChangeKind.Remove => 1,
            ChangeKind.Add => 2,
            ChangeKind.Protected => 3,
            _ => 4
        };
    }

    /// <summary>タイムスタンプを人が読みやすい形式へ変換する</summary>
    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>操作結果を利用者向けの日本語ラベルへ変換する。状態不明は失敗と区別して「不明」と表示する</summary>
    private static string StatusLabel(SyncOperationResult item)
    {
        return item.Uncertain ? "不明" : item.Succeeded ? "成功" : "失敗";
    }

    /// <summary>同期モードを利用者向けの日本語へ変換する</summary>
    private static string SyncModeLabel(SyncMode mode)
    {
        return SyncModePolicy.For(mode).ShortLabel;
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

}
