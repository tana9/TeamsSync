using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TeamsSync.Application.Abstractions;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
/// メインウィンドウ全体の状態(サインイン状態・ステータス表示・画面手順の進捗)を管理し、
/// 子ViewModel(チーム選択・メンバーリスト・同期ワークスペース)を束ねる。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly BusyOperationRunner _busyRunner;
    private readonly INotificationService _dialogs;
    private readonly IManualService _manual;
    private readonly ITeamsGateway _teamsGateway;
    private string? _currentUserId;

    /// <summary>ウィンドウタイトル(アプリ名とバージョン)。</summary>
    public string WindowTitle { get; } = $"TeamsSync {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    /// <summary>サインイン中のアカウント表示テキスト。</summary>
    [ObservableProperty] public partial string AccountText { get; set; } = "未サインイン";

    /// <summary>各種入力操作が有効かどうか。</summary>
    [ObservableProperty] public partial bool InputsEnabled { get; set; } = true;

    /// <summary>サインイン・サインアウト処理の実行中かどうか。</summary>
    [ObservableProperty] public partial bool IsBusy { get; set; }

    /// <summary>サインイン済みかどうか。</summary>
    [ObservableProperty] public partial bool IsSignedIn { get; set; }

    /// <summary>直近のステータスがエラーかどうか。</summary>
    [ObservableProperty] public partial bool IsStatusError { get; set; }

    /// <summary>画面下部に表示するステータスメッセージ。</summary>
    [ObservableProperty] public partial string StatusText { get; set; } = "サインインしてください";

    /// <summary>
    /// 子ViewModelのイベントを購読して連動させ、既定のステータスを設定する。
    /// </summary>
    public MainWindowViewModel(IAuthenticationService authentication, ITeamsGateway teamsGateway,
        INotificationService dialogs, IManualService manual, IUserPreferences preferences,
        TeamSelectionViewModel teamSelection, MemberFileViewModel memberFile, SyncWorkspaceViewModel syncWorkspace)
    {
        _authentication = authentication;
        _teamsGateway = teamsGateway;
        _dialogs = dialogs;
        _manual = manual;
        _busyRunner = new BusyOperationRunner(_dialogs, SetStatus, value => IsBusy = value);
        TeamSelection = teamSelection;
        MemberFile = memberFile;
        SyncWorkspace = syncWorkspace;

        TeamSelection.SelectionChanged += UpdateSyncContext;
        TeamSelection.ActivityChanged += _ => UpdateAvailability();
        TeamSelection.StatusChanged += SetStatus;
        MemberFile.DocumentChanged += UpdateSyncContext;
        MemberFile.StatusChanged += SetStatus;
        SyncWorkspace.ActivityChanged += _ => UpdateAvailability();
        SyncWorkspace.StatusChanged += SetStatus;
        // 手順3(同期モード)・手順4(同期差分)の完了状態はSyncWorkspace側の状態から決まるため、
        // 該当プロパティが変わるたびに手順表示(各カード直下のブロッカーメッセージの表示条件)を更新する。
        SyncWorkspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SyncWorkspaceViewModel.HasPlan) or nameof(SyncWorkspaceViewModel.HasSyncResult))
                NotifyWorkflowSteps();
        };
        if (preferences.LoadWarning is not null) SetStatus(preferences.LoadWarning, true);
        UpdateAvailability();
    }

    /// <summary>チーム選択画面のViewModel。</summary>
    public TeamSelectionViewModel TeamSelection { get; }

    /// <summary>メンバーリスト入力画面のViewModel。</summary>
    public MemberFileViewModel MemberFile { get; }

    /// <summary>同期ワークスペース(モード選択・差分・実行)のViewModel。</summary>
    public SyncWorkspaceViewModel SyncWorkspace { get; }

    /// <summary>未サインインかどうか(<see cref="IsSignedIn"/>の否定)。</summary>
    public bool IsNotSignedIn => !IsSignedIn;

    // 画面手順(1 チーム選択 → 2 メンバーリスト → 3 同期モード → 4 同期差分)の進捗状態。
    // 実際の操作順は厳密には固定していない(先にファイルを選んでもよい)ため、番号は「案内の順序」であり、
    // Current判定も前の手順が完了しているかどうかだけを見た簡易なガイドとして扱う。
    /// <summary>手順1(チーム選択)の進捗状態。</summary>
    public WorkflowStepState Step1State => TeamSelection.SelectedTeam is not null
        ? WorkflowStepState.Completed
        : IsSignedIn ? WorkflowStepState.Current : WorkflowStepState.Upcoming;

    /// <summary>手順2(メンバーリスト)の進捗状態。</summary>
    public WorkflowStepState Step2State => Step1State != WorkflowStepState.Completed
        ? WorkflowStepState.Upcoming
        : MemberFile.Document is not null ? WorkflowStepState.Completed : WorkflowStepState.Current;

    /// <summary>手順3(同期モード)の進捗状態。</summary>
    public WorkflowStepState Step3State => Step2State != WorkflowStepState.Completed
        ? WorkflowStepState.Upcoming
        : SyncWorkspace.HasPlan ? WorkflowStepState.Completed : WorkflowStepState.Current;

    partial void OnIsBusyChanged(bool value)
    {
        UpdateAvailability();
    }

    /// <summary>サインイン状態の変化に応じて、関連プロパティの通知と画面状態の更新を行う。</summary>
    partial void OnIsSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotSignedIn));
        UpdateSyncContext();
        UpdateAvailability();
    }

    /// <summary>対話型サインインを行い、自身の情報を取得してチーム一覧を初期化する。</summary>
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        await _busyRunner.RunAsync(async () =>
        {
            await _authentication.GetTokenAsync(true);
            var me = await _teamsGateway.GetMeAsync();
            _currentUserId = me.Id;
            AccountText = $"{me.DisplayName} ({me.UserPrincipalName})";
            IsSignedIn = true;
            await TeamSelection.InitializeAsync(me.Id);
        }, HandleAuthenticationException);
    }

    private bool CanSignIn()
    {
        return !IsBusy && !IsSignedIn;
    }

    /// <summary>サインアウトし、成功した場合のみ画面状態を未サインインへ戻す。</summary>
    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        // MSALのアカウント削除が失敗/キャンセルした場合、実際には認証情報が残っているため、
        // 画面だけを未サインイン状態にすると再サインアウトができなくなる。
        // RunAsyncの成功判定を見て、成功時のみ画面状態を未サインインへ揃える。
        var succeeded = await _busyRunner.RunAsync(() => _authentication.SignOutAsync(), HandleAuthenticationException);
        if (!succeeded) return;

        _currentUserId = null;
        TeamSelection.Clear();
        IsSignedIn = false;
        AccountText = "未サインイン";
        SetStatus("サインアウトしました", false);
        UpdateSyncContext();
    }

    private bool CanSignOut()
    {
        return !IsBusy && !SyncWorkspace.IsSyncing && IsSignedIn;
    }

    /// <summary>利用者向けマニュアルを開く。</summary>
    [RelayCommand]
    private void OpenManual()
    {
        try
        {
            _manual.OpenManual();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, "マニュアルを開けませんでした");
        }
    }

    /// <summary>認証設定エラーの場合に、専用のステータス・ダイアログタイトルを返す。</summary>
    private static (string Status, string DialogTitle)? HandleAuthenticationException(Exception ex)
    {
        return ex is AuthenticationConfigurationException
            ? ("サインインできませんでした。Entra IDのアプリ登録設定を管理者に確認してください",
                "Entra IDのアプリ登録設定を確認してください")
            : null;
    }

    /// <summary>選択中のチーム・メンバーリストの内容を同期ワークスペースへ反映する。</summary>
    private void UpdateSyncContext()
    {
        SyncWorkspace.SetContext(TeamSelection.SelectedTeam, MemberFile.Document, IsSignedIn,
            _authentication.TenantId, _currentUserId);
        NotifyWorkflowSteps();
    }

    /// <summary>画面手順1～3の進捗状態プロパティの変更を通知する。</summary>
    private void NotifyWorkflowSteps()
    {
        OnPropertyChanged(nameof(Step1State));
        OnPropertyChanged(nameof(Step2State));
        OnPropertyChanged(nameof(Step3State));
    }

    /// <summary>ステータスメッセージとエラー表示状態を設定する。</summary>
    private void SetStatus(string value, bool isError)
    {
        StatusText = value;
        IsStatusError = isError;
    }

    /// <summary>
    /// サインイン状態・実行中フラグから各種入力の有効/無効を判定し、子ViewModelとコマンドへ反映する。
    /// </summary>
    private void UpdateAvailability()
    {
        var syncActive = SyncWorkspace.IsBusy || SyncWorkspace.IsSyncing;
        InputsEnabled = IsSignedIn && !IsBusy && !TeamSelection.IsBusy && !syncActive;
        TeamSelection.SetExternalBusy(IsBusy || syncActive);
        MemberFile.SetEnabled(InputsEnabled);
        SyncWorkspace.SetExternalBusy(IsBusy || TeamSelection.IsBusy);
        SignInCommand.NotifyCanExecuteChanged();
        SignOutCommand.NotifyCanExecuteChanged();
    }
}