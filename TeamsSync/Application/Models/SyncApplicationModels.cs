using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Models;

/// <summary>同期プランを最新のチーム構成で再検証した結果</summary>
/// <param name="IsCurrent">元の同期プランをそのまま実行できるかどうか</param>
/// <param name="LatestPlan">最新のチーム構成から作成した同期プラン</param>
public sealed record SyncPlanRevalidation(bool IsCurrent, SyncPlan LatestPlan);

/// <summary>ファイルまたは貼り付け入力から読み取ったメンバーリスト</summary>
/// <param name="Addresses">重複を除いた氏名またはメールアドレスの一覧</param>
/// <param name="FileName">入力ファイル名。貼り付け入力の場合は表示用の名前</param>
/// <param name="FullPath">入力ファイルの絶対パス。貼り付け入力の場合は空文字</param>
/// <param name="LastModified">入力ファイルの最終更新日時</param>
/// <param name="SourceName">画面に表示する入力元の名前</param>
/// <param name="DetectedColumn">検出した列の表示名</param>
/// <param name="ContentSha256">入力内容のSHA-256ハッシュ値</param>
/// <param name="IsNameColumn">氏名の列として検出した場合はtrue</param>
public sealed record MemberListDocument(
    IReadOnlyList<string> Addresses,
    string FileName,
    string FullPath,
    DateTime LastModified,
    string SourceName,
    string DetectedColumn,
    string ContentSha256 = "",
    bool IsNameColumn = false);

/// <summary>同期実行を監査ログと関連付けるための情報</summary>
/// <param name="ExecutionId">同期実行を一意に識別するID</param>
/// <param name="InputFileName">入力ファイル名</param>
/// <param name="InputFileSha256">入力内容のSHA-256ハッシュ値</param>
/// <param name="TenantId">実行者のテナントID。取得できない場合はnull</param>
/// <param name="ActorObjectId">実行者のオブジェクトID。取得できない場合はnull</param>
public sealed record SyncAuditContext(
    Guid ExecutionId,
    string InputFileName,
    string InputFileSha256,
    string? TenantId = null,
    string? ActorObjectId = null);

/// <summary>同期処理の進捗を表す</summary>
/// <param name="Completed">完了した操作数</param>
/// <param name="Total">実行対象の総操作数</param>
/// <param name="Kind">直近に処理した操作の種別</param>
/// <param name="Email">直近に処理したユーザーのメールアドレス</param>
public sealed record SyncProgress(int Completed, int Total, ChangeKind Kind, string Email)
{
    /// <summary>完了率を0から100までの整数で表した値</summary>
    public int Percentage => Total == 0 ? 0 : (int)Math.Round(Completed * 100d / Total);
}

/// <summary>メンバー1名に対する同期操作の実行結果</summary>
/// <param name="Kind">実行した操作の種別</param>
/// <param name="Email">対象ユーザーのメールアドレス</param>
/// <param name="Succeeded">操作が成功した場合はtrue</param>
/// <param name="Error">失敗時のエラーメッセージ。成功した場合はnull</param>
/// <param name="DisplayName">対象ユーザーの表示名</param>
/// <param name="Uncertain">
///     Graphへの要求中にキャンセルされ、サーバー側で実際に成功したかどうか不明な場合はtrue。
///     <see cref="Succeeded" />はfalseになるが、実際には成功している可能性があるため
///     区別して表示・記録する必要がある
/// </param>
public sealed record SyncOperationResult(
    ChangeKind Kind,
    string Email,
    bool Succeeded,
    string? Error,
    string DisplayName = "",
    bool Uncertain = false);

/// <summary>1回の同期処理全体の実行結果</summary>
/// <param name="Operations">実行を開始した各操作の結果</param>
/// <param name="Cancelled">同期処理がキャンセルされた場合はtrue</param>
public sealed record SyncExecutionResult(IReadOnlyList<SyncOperationResult> Operations, bool Cancelled)
{
    /// <summary>成功した操作の件数</summary>
    public int SuccessCount => Operations.Count(x => x.Succeeded);

    /// <summary>失敗した操作の件数(状態不明を含む)</summary>
    public int FailureCount => Operations.Count(x => !x.Succeeded);

    /// <summary>キャンセルにより成否が確認できなかった操作の件数</summary>
    public int UncertainCount => Operations.Count(x => x.Uncertain);
}