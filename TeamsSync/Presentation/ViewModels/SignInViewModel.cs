using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     Microsoft Entra IDへのサインイン・サインアウトと、サインイン中のアカウント表示を管理する。
/// </summary>
public partial class SignInViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly BusyOperationRunner _busyRunner;
    private readonly Func<string, Task> _initializeTeamsAsync;
    private readonly ISyncResultWriter? _resultWriter;
    private readonly Action<string, bool> _reportStatus;
    private readonly ITeamsGateway _teamsGateway;
    private bool _externallyBusy;

    /// <summary>
    ///     コンストラクター。<paramref name="initializeTeamsAsync" />には、サインイン成功後に取得した
    ///     ユーザーIDでチーム選択を初期化する処理を渡す(サインインと同じ処理中表示・エラー処理の対象にするため)。
    /// </summary>
    public SignInViewModel(IAuthenticationService authentication, ITeamsGateway teamsGateway,
        INotificationService notifications, Action<string, bool> reportStatus, Func<string, Task> initializeTeamsAsync,
        ISyncResultWriter? resultWriter = null)
    {
        _authentication = authentication;
        _teamsGateway = teamsGateway;
        _reportStatus = reportStatus;
        _initializeTeamsAsync = initializeTeamsAsync;
        _resultWriter = resultWriter;
        _busyRunner = new BusyOperationRunner(notifications, reportStatus, value => IsBusy = value);
    }

    /// <summary>サインイン中のアカウント表示テキスト。</summary>
    [ObservableProperty]
    public partial string AccountText { get; set; } = "未サインイン";

    /// <summary>サインイン・サインアウト処理の実行中かどうか。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDiagnosticsCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>サインイン済みかどうか。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSignedIn))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDiagnosticsCommand))]
    public partial bool IsSignedIn { get; set; }

    /// <summary>未サインインかどうか(<see cref="IsSignedIn" />の否定)。</summary>
    public bool IsNotSignedIn => !IsSignedIn;

    /// <summary>サインイン中のテナントID。</summary>
    public string? TenantId => _authentication.TenantId;

    /// <summary>サインイン中のユーザーのオブジェクトID。</summary>
    public string? CurrentUserId { get; private set; }

    /// <summary>サインアウトが完了したときに発行される。</summary>
    public event Action? SignedOut;

    /// <summary>他画面の処理中状態を反映し、サインアウトコマンドの実行可否を再評価する。</summary>
    public void SetExternalBusy(bool value)
    {
        _externallyBusy = value;
        SignOutCommand.NotifyCanExecuteChanged();
        RunDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Graphの読み取り権限と主要APIへの接続を、データを変更せず確認する。</summary>
    [RelayCommand(CanExecute = nameof(CanRunDiagnostics))]
    private async Task RunDiagnosticsAsync()
    {
        await _busyRunner.RunAsync(async () =>
        {
            var me = await _teamsGateway.GetMeAsync();
            IReadOnlyList<TeamInfo> teams = await _teamsGateway.GetOwnedTeamsAsync(me.Id);
            int memberCount = 0;
            if (teams.Count > 0)
            {
                memberCount = (await _teamsGateway.GetTeamMembersAsync(teams[0].Id)).Count;
            }

            IReadOnlyList<DirectoryUser> users =
                await _teamsGateway.FindUsersAsync(me.UserPrincipalName);
            string logStatus = "ログ保存先 未確認";
            if (_resultWriter is not null)
            {
                _resultWriter.VerifyWriteAccess();
                logStatus = "ログ保存先 書き込み可";
            }

            _reportStatus(
                $"接続診断に成功しました（所有チーム {teams.Count}件／先頭チームのメンバー {memberCount}件／自分の検索結果 {users.Count}件／{logStatus}）",
                false);
        });
    }

    private bool CanRunDiagnostics() => IsSignedIn && !IsBusy && !_externallyBusy;

    /// <summary>対話型サインインを行い、自身の情報を取得してチーム選択を初期化する。</summary>
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        await _busyRunner.RunAsync(async () =>
        {
            await _authentication.GetTokenAsync(true);
            var me = await _teamsGateway.GetMeAsync();
            CurrentUserId = me.Id;
            AccountText = $"{me.DisplayName} ({me.UserPrincipalName})";
            IsSignedIn = true;
            await _initializeTeamsAsync(me.Id);
        }, HandleAuthenticationException);
    }

    private bool CanSignIn()
    {
        return !IsBusy && !IsSignedIn;
    }

    /// <summary>サインアウトし、成功した場合のみアカウント状態を未サインインへ戻す。</summary>
    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        // MSALのアカウント削除が失敗/キャンセルした場合、実際には認証情報が残っているため、
        // 画面だけを未サインイン状態にすると再サインアウトができなくなる。
        // RunAsyncの成功判定を見て、成功時のみ画面状態を未サインインへ揃える。
        bool succeeded =
            await _busyRunner.RunAsync(() => _authentication.SignOutAsync(), HandleAuthenticationException);
        if (!succeeded)
        {
            return;
        }

        CurrentUserId = null;
        IsSignedIn = false;
        AccountText = "未サインイン";
        _reportStatus("サインアウトしました", false);
        SignedOut?.Invoke();
    }

    private bool CanSignOut()
    {
        return !IsBusy && !_externallyBusy && IsSignedIn;
    }

    /// <summary>認証設定エラーの場合に、専用のステータス・ダイアログタイトルを返す。</summary>
    private static BusyOperationRunner.SpecificExceptionResult? HandleAuthenticationException(Exception ex)
    {
        return ex is AuthenticationConfigurationException
            ? new BusyOperationRunner.SpecificExceptionResult(
                "サインインできませんでした。Entra IDのアプリ登録設定を管理者に確認してください",
                "Entra IDのアプリ登録設定を確認してください", true)
            : null;
    }
}
