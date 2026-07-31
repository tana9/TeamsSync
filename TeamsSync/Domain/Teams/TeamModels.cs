namespace TeamsSync.Domain.Teams;

/// <summary>Teamsのチーム情報を表す。</summary>
/// <param name="Id">チームのオブジェクトID。</param>
/// <param name="DisplayName">表示名。</param>
/// <param name="Description">チームの説明(未設定の場合はnull)。</param>
public sealed record TeamInfo(string Id, string DisplayName, string? Description)
{
    /// <summary>UI表示用に<see cref="DisplayName"/>を返す。</summary>
    public override string ToString()
    {
        return DisplayName;
    }
}

/// <summary>チームの現メンバー1名分の情報を表す。</summary>
/// <param name="MembershipId">メンバーシップのオブジェクトID(削除操作に使用)。</param>
/// <param name="UserId">ユーザーのオブジェクトID。</param>
/// <param name="DisplayName">表示名。</param>
/// <param name="Email">メールアドレス(UPN)。</param>
/// <param name="IsOwner">チームの所有者かどうか。</param>
public sealed record TeamMember(string MembershipId, string UserId, string DisplayName, string Email, bool IsOwner);

/// <summary>Microsoft Entra IDのディレクトリ上のユーザー情報を表す。</summary>
/// <param name="Id">ユーザーのオブジェクトID。</param>
/// <param name="DisplayName">表示名。</param>
/// <param name="UserPrincipalName">ユーザープリンシパル名。</param>
/// <param name="Mail">メールアドレス(未設定の場合はnull)。</param>
public sealed record DirectoryUser(string Id, string DisplayName, string UserPrincipalName, string? Mail);

/// <summary>同期差分における1件のメンバー操作の種別。</summary>
public enum ChangeKind
{
    /// <summary>メンバーとして追加する。</summary>
    Add,

    /// <summary>メンバーから削除する。</summary>
    Remove,

    /// <summary>変更せずそのまま維持する。</summary>
    Keep,

    /// <summary>所有者のため保護され、変更されない。</summary>
    Protected,

    /// <summary>指定削除の対象として入力されたが、現在はチームに所属していない。</summary>
    NotMember,

    /// <summary>アドレスの解決に失敗した(未解決)。</summary>
    Error
}

/// <summary>同期の実行モード。</summary>
public enum SyncMode
{
    /// <summary>追加のみを行い、リストにない既存メンバーは維持する。</summary>
    AddOnly,

    /// <summary>入力リストで指定した一般メンバーだけを削除する。</summary>
    RemoveSpecified,

    /// <summary>完全同期。リストにない一般メンバーを削除する。</summary>
    FullSync
}

/// <summary>同期差分における1件のメンバー変更内容を表す。</summary>
/// <param name="Kind">操作の種別。</param>
/// <param name="DisplayName">対象ユーザーの表示名。</param>
/// <param name="Email">入力アドレス(またはメールアドレス)。</param>
/// <param name="Detail">画面表示用の詳細説明。</param>
/// <param name="UserId">対象ユーザーのオブジェクトID(判明している場合)。</param>
/// <param name="MembershipId">対象メンバーシップのオブジェクトID(判明している場合)。</param>
public sealed record SyncChange(
    ChangeKind Kind,
    string DisplayName,
    string Email,
    string Detail,
    string? UserId = null,
    string? MembershipId = null);

/// <summary>再検証(<see cref="TeamSyncService.RevalidatePlanAsync"/>)用の、
/// プラン作成時点のメンバーシップのスナップショット。</summary>
/// <param name="MembershipId">メンバーシップのオブジェクトID。</param>
/// <param name="UserId">ユーザーのオブジェクトID。</param>
/// <param name="IsOwner">所有者かどうか。</param>
public sealed record TeamMembershipSnapshot(string MembershipId, string UserId, bool IsOwner);

/// <summary>1回の同期で実行する変更内容をまとめた同期プラン。</summary>
/// <param name="Team">対象チーム。</param>
/// <param name="Changes">メンバーごとの変更内容一覧。</param>
/// <param name="InputAddresses">入力元のアドレス一覧。</param>
/// <param name="CurrentMemberCount">プラン作成時点の一般メンバー数。</param>
/// <param name="MembershipSnapshot">プラン作成時点のメンバーシップのスナップショット(再検証用)。</param>
/// <param name="Mode">同期の実行モード。</param>
public sealed record SyncPlan(
    TeamInfo Team,
    IReadOnlyList<SyncChange> Changes,
    IReadOnlyList<string> InputAddresses,
    int CurrentMemberCount = 0,
    IReadOnlyList<TeamMembershipSnapshot>? MembershipSnapshot = null,
    SyncMode Mode = SyncMode.FullSync)
{
    /// <summary>追加対象の件数。</summary>
    public int AddCount => Changes.Count(x => x.Kind == ChangeKind.Add);

    /// <summary>削除対象の件数。</summary>
    public int RemoveCount => Changes.Count(x => x.Kind == ChangeKind.Remove);

    /// <summary>変更なしの件数。</summary>
    public int KeepCount => Changes.Count(x => x.Kind == ChangeKind.Keep);

    /// <summary>所有者として保護された件数。</summary>
    public int ProtectedCount => Changes.Count(x => x.Kind == ChangeKind.Protected);

    /// <summary>指定削除の入力に含まれる、チーム未所属ユーザーの件数。</summary>
    public int NotMemberCount => Changes.Count(x => x.Kind == ChangeKind.NotMember);

    /// <summary>未解決(エラー)の件数。</summary>
    public int ErrorCount => Changes.Count(x => x.Kind == ChangeKind.Error);

    /// <summary>未解決の変更が1件以上あるかどうか。</summary>
    public bool HasErrors => ErrorCount > 0;
}

/// <summary>プレビュー時点のプランが最新の状態と一致しているかどうかの再検証結果。</summary>
/// <param name="IsCurrent">プレビュー時点のプランが最新の状態と一致しているかどうか。</param>
/// <param name="LatestPlan">再検証時点で作成し直した最新のプラン。</param>
public sealed record SyncPlanRevalidation(bool IsCurrent, SyncPlan LatestPlan);

/// <summary>読み込んだメンバーリストファイル(CSV/Excel)の内容を表す。</summary>
/// <param name="Addresses">抽出されたアドレス一覧。</param>
/// <param name="FileName">ファイル名。</param>
/// <param name="FullPath">フルパス。</param>
/// <param name="LastModified">最終更新日時。</param>
/// <param name="SourceName">入力元の種別(ファイル名またはテキスト貼り付けなど)を表す表示名。</param>
/// <param name="DetectedColumn">アドレスとして検出した列名。</param>
/// <param name="ContentSha256">内容のSHA-256ハッシュ(監査ログ用)。</param>
public sealed record MemberListDocument(
    IReadOnlyList<string> Addresses,
    string FileName,
    string FullPath,
    DateTime LastModified,
    string SourceName,
    string DetectedColumn,
    string ContentSha256 = "");

/// <summary>監査ログに記録する、同期実行1回分の文脈情報。</summary>
/// <param name="ExecutionId">実行を一意に識別するID。</param>
/// <param name="InputFileName">入力元のファイル名。</param>
/// <param name="InputFileSha256">入力内容のSHA-256ハッシュ。</param>
/// <param name="TenantId">実行時のテナントID。</param>
/// <param name="ActorObjectId">実行したユーザーのオブジェクトID。</param>
public sealed record SyncAuditContext(
    Guid ExecutionId,
    string InputFileName,
    string InputFileSha256,
    string? TenantId = null,
    string? ActorObjectId = null);

/// <summary>同期実行中の進捗状況を表す。</summary>
/// <param name="Completed">完了した操作数。</param>
/// <param name="Total">操作の総数。</param>
/// <param name="Kind">直近に処理した操作の種別。</param>
/// <param name="Email">直近に処理した対象のアドレス。</param>
public sealed record SyncProgress(int Completed, int Total, ChangeKind Kind, string Email)
{
    /// <summary>進捗率(0～100)。</summary>
    public int Percentage => Total == 0 ? 0 : (int)Math.Round(Completed * 100d / Total);
}

/// <summary>1件の追加・削除操作の実行結果を表す。</summary>
/// <param name="Kind">操作の種別。</param>
/// <param name="Email">対象のアドレス。</param>
/// <param name="Succeeded">成功したかどうか。</param>
/// <param name="Error">失敗時のエラーメッセージ。</param>
public sealed record SyncOperationResult(ChangeKind Kind, string Email, bool Succeeded, string? Error);

/// <summary>同期実行全体の結果を表す。</summary>
/// <param name="Operations">各操作の実行結果一覧。</param>
/// <param name="Cancelled">キャンセルにより途中で終了したかどうか。</param>
public sealed record SyncExecutionResult(IReadOnlyList<SyncOperationResult> Operations, bool Cancelled)
{
    /// <summary>成功した操作数。</summary>
    public int SuccessCount => Operations.Count(x => x.Succeeded);

    /// <summary>失敗した操作数。</summary>
    public int FailureCount => Operations.Count(x => !x.Succeeded);
}
