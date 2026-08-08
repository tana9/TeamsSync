using System.Text;

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
public sealed class SyncResultWriter(string? logDirectory = null, TimeProvider? timeProvider = null,
    IIdentifierGenerator? identifierGenerator = null) : ISyncResultWriter
{
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
    ///     実行日時・操作ユーザー・対象チーム・処理前メンバー一覧・追加削除の結果を持つCSVとして、
    ///     実行日時・実行ID・対象チーム名を含む一意なファイル名でログフォルダーへ書き出す。
    ///     最終照合結果は実行後に<see cref="AppendReconciliationResult" />で同じファイルへ追記する
    /// </summary>
    public string WriteAutoLog(SyncPlan plan, SyncOperationsResult result, SyncAuditContext auditContext)
    {
        Directory.CreateDirectory(_logDirectory);
        DateTimeOffset executedAt = _timeProvider.GetLocalNow();
        string stem = $"{executedAt:yyyyMMdd_HHmmss_fff}_{auditContext.ExecutionId:N}_{SanitizeFileName(plan.Team.DisplayName)}";
        string path = CreateUniquePath(stem);
        AtomicFileWriter.Write(path, stream => WriteCsv(stream, plan, result, auditContext, executedAt), false,
            _identifierGenerator);
        return path;
    }

    /// <inheritdoc />
    public void AppendReconciliationResult(string logPath, SyncPlan? remainingPlan, Exception? reconciliationError)
    {
        string existing = File.ReadAllText(logPath, new UTF8Encoding(true));
        string section = BuildReconciliationSection(remainingPlan, reconciliationError, _timeProvider.GetLocalNow());
        string updated = existing.TrimEnd('\r', '\n') + Environment.NewLine + Environment.NewLine + section;
        AtomicFileWriter.Write(logPath, stream =>
        {
            using StreamWriter writer = new(stream, new UTF8Encoding(true), leaveOpen: true);
            writer.Write(updated);
            writer.Flush();
        }, true, _identifierGenerator);
    }

    private static void WriteCsv(Stream stream, SyncPlan plan, SyncOperationsResult result,
        SyncAuditContext auditContext, DateTimeOffset executedAt)
    {
        using StreamWriter writer = new(stream, new UTF8Encoding(true), leaveOpen: true);
        WriteExecutionInfoSection(writer, plan, auditContext, executedAt);
        writer.WriteLine();
        WritePreSyncMembersSection(writer, plan);
        writer.WriteLine();
        WriteOperationsSection(writer, plan, result);
        writer.Flush();
    }

    /// <summary>実行日時・操作ユーザー・対象チーム・同期モードを記録するセクションを出力する</summary>
    private static void WriteExecutionInfoSection(TextWriter writer, SyncPlan plan, SyncAuditContext auditContext,
        DateTimeOffset executedAt)
    {
        writer.WriteLine("実行情報");
        writer.WriteLine("実行日時,実行ID,操作ユーザー表示名,操作ユーザーオブジェクトID,対象チーム,同期モード");
        writer.WriteLine(string.Join(",",
            Csv(FormatTimestamp(executedAt)),
            Csv(auditContext.ExecutionId.ToString()),
            Csv(auditContext.ActorDisplayName ?? ""),
            Csv(auditContext.ActorObjectId ?? ""),
            Csv(plan.Team.DisplayName),
            Csv(SyncModeLabel(plan.Mode))));
    }

    /// <summary>プラン作成(=同期実行直前)の時点でのチーム全メンバーを記録するセクションを出力する</summary>
    private static void WritePreSyncMembersSection(TextWriter writer, SyncPlan plan)
    {
        writer.WriteLine("処理前メンバー一覧");
        writer.WriteLine("表示名,メールアドレス,区分");
        foreach (TeamMember member in plan.CurrentMembersOrEmpty)
        {
            writer.WriteLine(string.Join(",", Csv(member.DisplayName), Csv(member.Email),
                Csv(member.IsOwner ? "所有者" : "一般")));
        }
    }

    /// <summary>チーム名・モード・操作種別・アドレス・成否・エラーの列を持つ同期操作結果セクションを出力する</summary>
    private static void WriteOperationsSection(TextWriter writer, SyncPlan plan, SyncOperationsResult result)
    {
        writer.WriteLine("同期操作");
        writer.WriteLine("チーム,同期モード,操作,表示名,メールアドレス,結果,エラー");
        IReadOnlyList<SyncChange> plannedOperations = plan.Operations;
        for (int index = 0; index < plannedOperations.Count; index++)
        {
            SyncChange planned = plannedOperations[index];
            SyncOperationResult? executed = index < result.Results.Count ? result.Results[index] : null;
            WriteRow(writer, plan, planned.Kind, planned.DisplayName, planned.Email,
                executed is null ? "未実行" : StatusLabel(executed),
                executed is null
                    ? result.Cancelled ? "同期がキャンセルされたため未実行" : "実行結果が記録されていません"
                    : executed.Error ?? "");
        }
    }

    /// <summary>
    ///     最終照合(実行後の最新状態取得)の結果セクションを組み立てる。取得自体に失敗した場合はその旨を、
    ///     成功した場合は未反映の追加・削除が残っていないかを記録する
    /// </summary>
    private static string BuildReconciliationSection(SyncPlan? remainingPlan, Exception? reconciliationError,
        DateTimeOffset checkedAt)
    {
        StringBuilder section = new();
        section.AppendLine("最終照合結果");
        section.AppendLine("確認日時,結果");
        section.AppendLine(string.Join(",", Csv(FormatTimestamp(checkedAt)),
            Csv(ReconciliationStatusLabel(remainingPlan, reconciliationError))));

        if (remainingPlan is { } plan && (plan.AddCount > 0 || plan.RemoveCount > 0))
        {
            section.AppendLine();
            section.AppendLine("未反映の差分");
            section.AppendLine("操作,表示名,メールアドレス");
            foreach (SyncChange change in plan.Operations)
            {
                section.AppendLine(string.Join(",", Csv(OperationLabel(change.Kind)), Csv(change.DisplayName),
                    Csv(change.Email)));
            }
        }

        return section.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>最終照合の結果を利用者向けの日本語へ変換する</summary>
    private static string ReconciliationStatusLabel(SyncPlan? remainingPlan, Exception? reconciliationError)
    {
        if (reconciliationError is not null)
        {
            return $"照合に失敗しました: {reconciliationError.Message}";
        }

        if (remainingPlan is null)
        {
            return "照合結果が記録されていません";
        }

        return remainingPlan.AddCount == 0 && remainingPlan.RemoveCount == 0
            ? "目的の状態に到達しています(未反映の差分なし)"
            : $"未反映の差分が残っています(追加待ち{remainingPlan.AddCount}件、削除待ち{remainingPlan.RemoveCount}件)";
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

    /// <summary>同期操作1件をCSVへ出力する</summary>
    private static void WriteRow(TextWriter writer, SyncPlan plan, ChangeKind kind, string displayName,
        string email, string status, string error)
    {
        writer.WriteLine(string.Join(",", Csv(plan.Team.DisplayName), Csv(SyncModeLabel(plan.Mode)),
            Csv(OperationLabel(kind)), Csv(displayName), Csv(email), Csv(status), Csv(error)));
    }

    /// <summary>同期モードを利用者向けの日本語へ変換する</summary>
    private static string SyncModeLabel(SyncMode mode)
    {
        return SyncModePolicy.For(mode).ShortLabel;
    }

    /// <summary>実行した操作を利用者向けの日本語へ変換する</summary>
    private static string OperationLabel(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Add or ChangeKind.Remove => ChangeKindText.Label(kind),
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
}