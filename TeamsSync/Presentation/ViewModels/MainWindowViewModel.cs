using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Services;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     メインウィンドウ全体の状態(ステータス表示・入力の有効化)を管理し、
///     子ViewModel(サインイン・チーム選択・メンバーリスト・同期ワークスペース)を束ねる。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    ///     子ViewModelのイベントを購読して連動させ、既定のステータスを設定する。
    /// </summary>
    public MainWindowViewModel(IAuthenticationService authentication, TeamsAccessService teamsAccess,
        INotificationService dialogs, IUserPreferences preferences,
        TeamSelectionViewModel teamSelection, MemberFileViewModel memberFile, SyncWorkspaceViewModel syncWorkspace,
        ManualViewModel manual)
    {
        TeamSelection = teamSelection;
        MemberFile = memberFile;
        SyncWorkspace = syncWorkspace;
        Manual = manual;
        SignIn = new SignInViewModel(authentication, teamsAccess, dialogs, SetStatus,
            userId => TeamSelection.InitializeAsync(userId));
        WorkflowSteps = new WorkflowStepsViewModel(SignIn, TeamSelection, MemberFile, SyncWorkspace);

        // SelectedTeam/IsBusyはMVVM Toolkitの[ObservableProperty]が自動でPropertyChangedを発行するため、
        // 専用のカスタムイベントを設けず、子ViewModelのPropertyChangedをプロパティ名でフィルターして購読する。
        SignIn.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(SignInViewModel.IsBusy):
                    UpdateAvailability();
                    break;
                case nameof(SignInViewModel.IsSignedIn):
                    UpdateSyncContext();
                    UpdateAvailability();
                    break;
            }
        };
        SignIn.SignedOut += () => TeamSelection.Clear();
        TeamSelection.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(TeamSelectionViewModel.SelectedTeam):
                    UpdateSyncContext();
                    break;
                case nameof(TeamSelectionViewModel.IsBusy):
                    UpdateAvailability();
                    break;
            }
        };
        TeamSelection.StatusChanged += SetStatus;
        MemberFile.DocumentChanged += UpdateSyncContext;
        MemberFile.StatusChanged += SetStatus;
        MemberFile.Import.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TeamMemberImportViewModel.IsImportingMembers))
            {
                UpdateAvailability();
            }
        };
        SyncWorkspace.StatusChanged += SetStatus;
        SyncWorkspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SyncWorkspaceViewModel.IsBusy) or nameof(SyncWorkspaceViewModel.IsSyncing))
            {
                UpdateAvailability();
            }
        };
        if (preferences.LoadWarning is not null)
        {
            SetStatus(preferences.LoadWarning, true);
        }

        UpdateAvailability();
    }

    /// <summary>ウィンドウタイトル(アプリ名とバージョン)。</summary>
    public string WindowTitle { get; } = $"TeamsSync {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    /// <summary>各種入力操作が有効かどうか。</summary>
    [ObservableProperty]
    public partial bool InputsEnabled { get; set; } = true;

    /// <summary>直近のステータスがエラーかどうか。</summary>
    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    /// <summary>画面下部に表示するステータスメッセージ。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "サインインしてください";

    /// <summary>サインイン状態・アカウント表示を管理するViewModel。</summary>
    public SignInViewModel SignIn { get; }

    /// <summary>チーム選択画面のViewModel。</summary>
    public TeamSelectionViewModel TeamSelection { get; }

    /// <summary>メンバーリスト入力画面のViewModel。</summary>
    public MemberFileViewModel MemberFile { get; }

    /// <summary>同期ワークスペース(モード選択・差分・実行)のViewModel。</summary>
    public SyncWorkspaceViewModel SyncWorkspace { get; }

    /// <summary>画面手順(1〜3)の進捗状態を算出するViewModel。</summary>
    public WorkflowStepsViewModel WorkflowSteps { get; }

    /// <summary>利用者向けマニュアルを開く操作を管理するViewModel。</summary>
    public ManualViewModel Manual { get; }

    /// <summary>選択中のチーム・メンバーリストの内容を同期ワークスペースへ反映する。</summary>
    private void UpdateSyncContext()
    {
        MemberFile.SetSelectedTeam(TeamSelection.SelectedTeam);
        SyncWorkspace.SetContext(TeamSelection.SelectedTeam, MemberFile.Document, SignIn.IsSignedIn,
            SignIn.TenantId, SignIn.CurrentUserId);
    }

    /// <summary>ステータスメッセージとエラー表示状態を設定する。</summary>
    private void SetStatus(string value, bool isError)
    {
        StatusText = value;
        IsStatusError = isError;
    }

    /// <summary>
    ///     サインイン状態・実行中フラグから各種入力の有効/無効を判定し、子ViewModelへ反映する。
    /// </summary>
    private void UpdateAvailability()
    {
        bool memberImportActive = MemberFile.Import.IsImportingMembers;
        bool syncActive = SyncWorkspace.IsBusy || SyncWorkspace.IsSyncing;
        InputsEnabled = SignIn.IsSignedIn && !SignIn.IsBusy && !TeamSelection.IsBusy && !syncActive;
        InputsEnabled &= !memberImportActive;
        TeamSelection.SetExternalBusy(SignIn.IsBusy || syncActive || memberImportActive);
        MemberFile.SetEnabled(InputsEnabled);
        SyncWorkspace.SetExternalBusy(SignIn.IsBusy || TeamSelection.IsBusy || memberImportActive);
        SignIn.SetExternalBusy(SyncWorkspace.IsSyncing || memberImportActive);
    }
}
