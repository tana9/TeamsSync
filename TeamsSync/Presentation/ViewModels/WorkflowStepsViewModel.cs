using CommunityToolkit.Mvvm.ComponentModel;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
/// 画面手順(1 チーム選択 → 2 メンバーリスト → 3 同期モード → 4 同期差分)の進捗状態を、
/// 各画面領域のViewModelの状態から算出する。
/// </summary>
public sealed class WorkflowStepsViewModel : ObservableObject
{
    private readonly SignInViewModel _signIn;
    private readonly TeamSelectionViewModel _teamSelection;
    private readonly MemberFileViewModel _memberFile;
    private readonly SyncWorkspaceViewModel _syncWorkspace;

    /// <summary>コンストラクター。各画面領域のViewModelの変化を購読し、手順の進捗状態を追従させる。</summary>
    public WorkflowStepsViewModel(SignInViewModel signIn, TeamSelectionViewModel teamSelection,
        MemberFileViewModel memberFile, SyncWorkspaceViewModel syncWorkspace)
    {
        _signIn = signIn;
        _teamSelection = teamSelection;
        _memberFile = memberFile;
        _syncWorkspace = syncWorkspace;

        signIn.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SignInViewModel.IsSignedIn)) NotifySteps();
        };
        teamSelection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TeamSelectionViewModel.SelectedTeam)) NotifySteps();
        };
        memberFile.DocumentChanged += NotifySteps;
        syncWorkspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SyncWorkspaceViewModel.HasPlan) or nameof(SyncWorkspaceViewModel.HasSyncResult))
                NotifySteps();
        };
    }

    // 実際の操作順は厳密には固定していない(先にファイルを選んでもよい)ため、番号は「案内の順序」であり、
    // Current判定も前の手順が完了しているかどうかだけを見た簡易なガイドとして扱う。
    /// <summary>手順1(チーム選択)の進捗状態。</summary>
    public WorkflowStepState Step1State => _teamSelection.SelectedTeam is not null
        ? WorkflowStepState.Completed
        : _signIn.IsSignedIn ? WorkflowStepState.Current : WorkflowStepState.Upcoming;

    /// <summary>手順2(メンバーリスト)の進捗状態。</summary>
    public WorkflowStepState Step2State => Step1State != WorkflowStepState.Completed
        ? WorkflowStepState.Upcoming
        : _memberFile.Document is not null ? WorkflowStepState.Completed : WorkflowStepState.Current;

    /// <summary>手順3(同期モード)の進捗状態。</summary>
    public WorkflowStepState Step3State => Step2State != WorkflowStepState.Completed
        ? WorkflowStepState.Upcoming
        : _syncWorkspace.HasPlan ? WorkflowStepState.Completed : WorkflowStepState.Current;

    private void NotifySteps()
    {
        OnPropertyChanged(nameof(Step1State));
        OnPropertyChanged(nameof(Step2State));
        OnPropertyChanged(nameof(Step3State));
    }
}
