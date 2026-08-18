using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Services;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     メインウィンドウ全体の状態(ステータス表示・入力の有効化)を管理し、
///     子ViewModel(サインイン・チーム選択・メンバーリスト・同期ワークスペース)を束ねる
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    ///     子ViewModelのイベントを購読して連動させ、既定のステータスを設定する
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
        // WhenChangedは「どのプロパティの変化に何を反応させるか」だけを1行で表す拡張メソッド
        // (WorkflowStepsViewModelも一部同じ発生源(SignIn.IsSignedIn等)を独立に購読しているが、
        // 目的が異なる(こちらは入力有効化、あちらは手順進捗表示)ため、あえて別々の購読のままにしている)
        SignIn.WhenChanged(nameof(SignInViewModel.IsBusy), UpdateAvailability);
        SignIn.WhenChanged(nameof(SignInViewModel.IsSignedIn), () =>
        {
            UpdateSyncContext();
            UpdateAvailability();
        });
        SignIn.SignedOut += () => TeamSelection.Clear();
        TeamSelection.WhenChanged(nameof(TeamSelectionViewModel.SelectedTeam), UpdateSyncContext);
        TeamSelection.WhenChanged(nameof(TeamSelectionViewModel.IsBusy), UpdateAvailability);
        TeamSelection.StatusChanged += SetStatus;
        MemberFile.DocumentChanged += UpdateSyncContext;
        MemberFile.StatusChanged += SetStatus;
        MemberFile.Import.WhenChanged(nameof(TeamMemberImportViewModel.IsImportingMembers), UpdateAvailability);
        MemberFile.Export.WhenChanged(nameof(TeamMemberExportViewModel.IsExporting), UpdateAvailability);
        SyncWorkspace.StatusChanged += SetStatus;
        SyncWorkspace.Preview.WhenChanged(nameof(SyncPreviewDisplayState.IsBusy), UpdateAvailability);
        SyncWorkspace.Execution.WhenChanged(nameof(SyncExecutionDisplayState.IsBusy), UpdateAvailability);
        if (preferences.LoadWarning is not null)
        {
            SetStatus(preferences.LoadWarning, true);
        }

        UpdateAvailability();
    }

    /// <summary>ウィンドウタイトル(アプリ名とバージョン)</summary>
    public string WindowTitle { get; } = $"TeamsSync {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    /// <summary>各種入力操作が有効かどうか</summary>
    [ObservableProperty]
    public partial bool InputsEnabled { get; set; } = true;

    /// <summary>直近のステータスがエラーかどうか</summary>
    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    /// <summary>画面下部に表示するステータスメッセージ</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "サインインしてください";

    /// <summary>サインイン状態・アカウント表示を管理するViewModel</summary>
    public SignInViewModel SignIn { get; }

    /// <summary>チーム選択画面のViewModel</summary>
    public TeamSelectionViewModel TeamSelection { get; }

    /// <summary>メンバーリスト入力画面のViewModel</summary>
    public MemberFileViewModel MemberFile { get; }

    /// <summary>同期ワークスペース(モード選択・差分・実行)のViewModel</summary>
    public SyncWorkspaceViewModel SyncWorkspace { get; }

    /// <summary>画面手順(1〜3)の進捗状態を算出するViewModel</summary>
    public WorkflowStepsViewModel WorkflowSteps { get; }

    /// <summary>利用者向けマニュアルを開く操作を管理するViewModel</summary>
    public ManualViewModel Manual { get; }

    /// <summary>選択中のチーム・メンバーリストの内容を同期ワークスペースへ反映する</summary>
    private void UpdateSyncContext()
    {
        MemberFile.SetSelectedTeam(TeamSelection.SelectedTeam);
        SyncWorkspace.SetContext(TeamSelection.SelectedTeam, MemberFile.Document, SignIn.IsSignedIn,
            SignIn.TenantId, SignIn.CurrentUserId, SignIn.CurrentUserDisplayName);
    }

    /// <summary>ステータスメッセージとエラー表示状態を設定する</summary>
    private void SetStatus(string value, bool isError)
    {
        StatusText = value;
        IsStatusError = isError;
    }

    /// <summary>
    ///     サインイン状態・実行中フラグから各種入力の有効/無効を判定し、子ViewModelへ反映する
    /// </summary>
    private void UpdateAvailability()
    {
        bool memberImportActive = MemberFile.Import.IsImportingMembers;
        // メンバー一覧のCSV出力中も、Importと同様にサインアウト・チーム切替・同期実行開始を
        // ブロックする。以前はIsExportingがどこにも波及せず、出力中でもこれらの操作が
        // 無調整のまま実行できてしまっていた
        bool memberExportActive = MemberFile.Export.IsExporting;
        bool syncActive = SyncWorkspace.Preview.IsBusy || SyncWorkspace.Execution.IsBusy;
        InputsEnabled = SignIn is { IsSignedIn: true, IsBusy: false } && !TeamSelection.IsBusy && !syncActive;
        InputsEnabled &= !memberImportActive;
        TeamSelection.SetExternalBusy(SignIn.IsBusy || syncActive || memberImportActive || memberExportActive);
        MemberFile.SetEnabled(InputsEnabled);
        SyncWorkspace.SetExternalBusy(SignIn.IsBusy || TeamSelection.IsBusy || memberImportActive || memberExportActive);
        SignIn.SetExternalBusy(SyncWorkspace.Execution.IsBusy || memberImportActive || memberExportActive);
    }
}