using CommunityToolkit.Mvvm.ComponentModel;

using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     画面手順のうち手順1〜3(1 チーム選択 → 2 メンバーリスト → 3 同期モード)の進捗状態を、
///     各画面領域のViewModelの状態から算出する。手順4(同期差分)の進捗は
///     <see cref="SyncWorkspaceViewModel" />のPlan/Resultが個別に管理するため、ここでは扱わない
/// </summary>
public sealed class WorkflowStepsViewModel : ObservableObject
{
    private readonly MemberFileViewModel _memberFile;
    private readonly SignInViewModel _signIn;
    private readonly SyncWorkspaceViewModel _syncWorkspace;
    private readonly TeamSelectionViewModel _teamSelection;

    /// <summary>コンストラクター。各画面領域のViewModelの変化を購読し、手順の進捗状態を追従させる</summary>
    public WorkflowStepsViewModel(SignInViewModel signIn, TeamSelectionViewModel teamSelection,
        MemberFileViewModel memberFile, SyncWorkspaceViewModel syncWorkspace)
    {
        _signIn = signIn;
        _teamSelection = teamSelection;
        _memberFile = memberFile;
        _syncWorkspace = syncWorkspace;

        signIn.WhenChanged(nameof(SignInViewModel.IsSignedIn), NotifySteps);
        teamSelection.WhenChanged(nameof(TeamSelectionViewModel.SelectedTeam), NotifySteps);
        memberFile.DocumentChanged += NotifySteps;
        syncWorkspace.Plan.WhenChanged(nameof(SyncPlanDisplayState.HasPlan), NotifySteps);
    }

    // 実際の操作順は厳密には固定していない(先にファイルを選んでもよい)ため、番号は「案内の順序」であり、
    // Current判定も前の手順が完了しているかどうかだけを見た簡易なガイドとして扱う
    /// <summary>手順1(チーム選択)の進捗状態</summary>
    public WorkflowStepState Step1State => _teamSelection.SelectedTeam is not null
        ? WorkflowStepState.Completed
        : _signIn.IsSignedIn
            ? WorkflowStepState.Current
            : WorkflowStepState.Upcoming;

    /// <summary>手順2(メンバーリスト)の進捗状態</summary>
    public WorkflowStepState Step2State => Step1State != WorkflowStepState.Completed
        ? WorkflowStepState.Upcoming
        : _memberFile.Document is not null
            ? WorkflowStepState.Completed
            : WorkflowStepState.Current;

    /// <summary>手順3(同期モード)の進捗状態</summary>
    public WorkflowStepState Step3State => Step2State != WorkflowStepState.Completed
        ? WorkflowStepState.Upcoming
        : _syncWorkspace.Plan.HasPlan
            ? WorkflowStepState.Completed
            : WorkflowStepState.Current;

    private void NotifySteps()
    {
        OnPropertyChanged(nameof(Step1State));
        OnPropertyChanged(nameof(Step2State));
        OnPropertyChanged(nameof(Step3State));
    }
}