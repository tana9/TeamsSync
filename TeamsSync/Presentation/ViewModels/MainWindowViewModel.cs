using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TeamsSync.Application.Abstractions;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly BusyOperationRunner _busyRunner;
    private readonly INotificationService _dialogs;
    private readonly IManualService _manual;
    private readonly ITeamsGateway _teamsGateway;
    private string? _currentUserId;

    public string WindowTitle { get; } = $"TeamsSync {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    [ObservableProperty] private string accountText = "未サインイン";
    [ObservableProperty] private bool inputsEnabled = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isSignedIn;
    [ObservableProperty] private bool isStatusError;
    [ObservableProperty] private string statusText = "サインインしてください";

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

    public TeamSelectionViewModel TeamSelection { get; }
    public MemberFileViewModel MemberFile { get; }
    public SyncWorkspaceViewModel SyncWorkspace { get; }

    public bool IsNotSignedIn => !IsSignedIn;

    // 画面手順(1 チーム選択 → 2 メンバーリスト → 3 同期モード → 4 同期差分)の進捗状態。
    // 実際の操作順は厳密には固定していない(先にファイルを選んでもよい)ため、番号は「案内の順序」であり、
    // Current判定も前の手順が完了しているかどうかだけを見た簡易なガイドとして扱う。
    public WorkflowStepState Step1State => TeamSelection.SelectedTeam is not null
        ? WorkflowStepState.Completed
        : IsSignedIn ? WorkflowStepState.Current : WorkflowStepState.Upcoming;

    public WorkflowStepState Step2State => Step1State != WorkflowStepState.Completed
        ? WorkflowStepState.Upcoming
        : MemberFile.Document is not null ? WorkflowStepState.Completed : WorkflowStepState.Current;

    public WorkflowStepState Step3State => Step2State != WorkflowStepState.Completed
        ? WorkflowStepState.Upcoming
        : SyncWorkspace.HasPlan ? WorkflowStepState.Completed : WorkflowStepState.Current;

    partial void OnIsBusyChanged(bool value)
    {
        UpdateAvailability();
    }

    partial void OnIsSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotSignedIn));
        UpdateSyncContext();
        UpdateAvailability();
    }

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

    private static (string Status, string DialogTitle)? HandleAuthenticationException(Exception ex)
    {
        return ex is AuthenticationConfigurationException
            ? ("サインインできませんでした。Entra IDのアプリ登録設定を管理者に確認してください",
                "Entra IDのアプリ登録設定を確認してください")
            : null;
    }

    private void UpdateSyncContext()
    {
        SyncWorkspace.SetContext(TeamSelection.SelectedTeam, MemberFile.Document, IsSignedIn,
            _authentication.TenantId, _currentUserId);
        NotifyWorkflowSteps();
    }

    private void NotifyWorkflowSteps()
    {
        OnPropertyChanged(nameof(Step1State));
        OnPropertyChanged(nameof(Step2State));
        OnPropertyChanged(nameof(Step3State));
    }

    private void SetStatus(string value, bool isError)
    {
        StatusText = value;
        IsStatusError = isError;
    }

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