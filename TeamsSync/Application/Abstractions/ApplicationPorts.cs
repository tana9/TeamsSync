using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Abstractions;

/// <summary>Entra IDのアプリ登録設定に起因して認証を行えない場合にスローされる例外。</summary>
public sealed class AuthenticationConfigurationException(string message) : InvalidOperationException(message);

/// <summary>Microsoft Entra IDでのサインイン・サインアウトおよびトークン取得を担う。</summary>
public interface IAuthenticationService
{
    /// <summary>サインイン中のユーザー表示名(未サインインの場合はnull)。</summary>
    string? UserName { get; }

    /// <summary>サインイン中のテナントID(未サインインの場合はnull)。</summary>
    string? TenantId { get; }

    /// <summary>アクセストークンを取得する。必要に応じて対話的サインインを行う。</summary>
    Task<string> GetTokenAsync(bool interactive = false, CancellationToken cancellationToken = default);

    /// <summary>サインアウトし、キャッシュされた認証情報を削除する。</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}

/// <summary>Microsoft Graph APIを介したチーム・メンバー情報の取得と変更を担う。</summary>
public interface ITeamsGateway
{
    /// <summary>サインイン中のユーザー自身の情報を取得する。</summary>
    Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>指定したユーザーが所有者になっているチームの一覧を取得する。</summary>
    Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
        CancellationToken cancellationToken = default);

    /// <summary>指定したチームの現メンバー一覧を取得する。</summary>
    Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId, CancellationToken cancellationToken = default);

    /// <summary>氏名またはメールアドレスに一致するディレクトリ上のユーザーを検索する。</summary>
    Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>指定したユーザーをチームの一般メンバーとして追加する。</summary>
    Task AddMemberAsync(string teamId, string userId, CancellationToken cancellationToken = default);

    /// <summary>指定したメンバーシップをチームから削除する。</summary>
    Task RemoveMemberAsync(string teamId, string membershipId, CancellationToken cancellationToken = default);

    /// <summary>所有チーム判定のキャッシュを消去する。既定実装では何も行わない。</summary>
    void ClearOwnedTeamsCache(string? currentUserId = null)
    {
    }
}

/// <summary>CSV/Excelのメンバーリストファイルを読み込む。</summary>
public interface IMemberListReader
{
    // ファイル解析は行数・列数によって時間がかかりうるため、UIスレッド外実行とキャンセルを呼び出し元が制御できるようにする
    /// <summary>指定したパスのメンバーリストファイルを読み込み、アドレス一覧へ変換する。</summary>
    MemberListDocument Read(string path, CancellationToken cancellationToken);
}

/// <summary>テキストとして貼り付けられたメンバーリストを解析する。</summary>
public interface IMemberTextParser
{
    /// <summary>貼り付けられたテキストを解析し、アドレス一覧へ変換する。</summary>
    MemberListDocument Parse(string text, CancellationToken cancellationToken);
}

/// <summary>同期結果をログファイルへ自動的に記録する。</summary>
public interface ISyncResultWriter
{
    /// <summary>同期プランと実行結果を一意なログファイルへ書き出し、保存したファイルのフルパスを返す。</summary>
    string WriteAutoLog(SyncPlan plan, SyncExecutionResult result, Guid executionId);

    /// <summary>ログ保存先を作成し、一時ファイルへ書き込めることを確認する。</summary>
    void VerifyWriteAccess()
    {
    }
}

/// <summary>ユーザーごとの永続設定(最終利用フォルダーなど)を扱う。</summary>
public interface IUserPreferences
{
    /// <summary>設定の読み込み時に発生した警告メッセージ(なければnull)。</summary>
    string? LoadWarning => null;

    /// <summary>最後にファイル選択に使用したフォルダー。</summary>
    string? LastFolder { get; set; }

    /// <summary>現在の設定値を永続化する。</summary>
    void Save();
}
