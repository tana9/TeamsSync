using System.Windows.Threading;

using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Files;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void MainWindow_IsNotSignedIn_初期状態でtrueでありサインイン後にfalseになる()
    {
        FakeTeamsGateway gateway = new();
        FakeDialogs dialogs = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        MemberFileViewModel memberFile = new(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs, gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        MainWindowViewModel viewModel = new(new FakeAuthenticationService(), gateway, dialogs,
            preferences, teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new FakeManualService(), dialogs));

        Assert.True(viewModel.SignIn.IsNotSignedIn);

        viewModel.SignIn.IsSignedIn = true;

        Assert.False(viewModel.SignIn.IsNotSignedIn);
    }

    [Fact]
    public async Task MainWindow_メンバー一覧出力中はチーム選択と同期実行がブロックされる()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeTeamsGateway gateway = new()
        {
            OnGetMembers = async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return [];
            }
        };
        FakeDialogs dialogs = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        MemberFileViewModel memberFile = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        MainWindowViewModel viewModel = new(new FakeAuthenticationService(), gateway, dialogs,
            new FakePreferences(), teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new FakeManualService(), dialogs));
        viewModel.SignIn.IsSignedIn = true;
        teamSelection.SelectedTeam = new TeamInfo("team-1", "開発", null);

        Task exporting = memberFile.Export.ExportCommand.ExecuteAsync(null);
        await started.Task;

        Assert.False(teamSelection.IsSelectionEnabled);
        Assert.False(viewModel.SignIn.SignOutCommand.CanExecute(null));

        release.TrySetResult();
        await exporting;

        Assert.True(teamSelection.IsSelectionEnabled);
    }

    [Fact]
    public void MainWindow_OpenManual_成功時はエラーダイアログを表示しない()
    {
        FakeTeamsGateway gateway = new();
        FakeDialogs dialogs = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        MemberFileViewModel memberFile = new(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs, gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        MainWindowViewModel viewModel = new(new FakeAuthenticationService(), gateway, dialogs,
            preferences, teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new FakeManualService(), dialogs));

        viewModel.Manual.OpenManualCommand.Execute(null);
    }

    [Fact]
    public void MainWindow_OpenManual_失敗時はエラーダイアログを表示する()
    {
        FakeTeamsGateway gateway = new();
        FakeDialogs dialogs = new();
        RecordingNotificationService notifications = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        MemberFileViewModel memberFile = new(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs, gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        MainWindowViewModel viewModel = new(new FakeAuthenticationService(), gateway, notifications,
            preferences, teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new ThrowingManualService(), notifications));

        viewModel.Manual.OpenManualCommand.Execute(null);

        Assert.Equal("展開に失敗しました", notifications.ErrorMessage);
        Assert.Equal("マニュアルを開けませんでした", notifications.ErrorTitle);
    }

    [Fact]
    public async Task MainWindow_SignOut_成功時は未サインイン状態になりチーム選択もクリアされる()
    {
        FakeTeamsGateway gateway = new() { OwnedTeams = [new TeamInfo("team-1", "開発", null)] };
        FakeDialogs dialogs = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        MemberFileViewModel memberFile = new(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs, gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        MainWindowViewModel viewModel = new(new FakeAuthenticationService(), gateway, dialogs,
            preferences, teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new FakeManualService(), dialogs));
        await teamSelection.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        teamSelection.SelectedTeam = teamSelection.Teams[0];
        viewModel.SignIn.IsSignedIn = true;
        viewModel.SignIn.AccountText = "Current User (current@example.com)";

        await viewModel.SignIn.SignOutCommand.ExecuteAsync(null);

        Assert.False(viewModel.SignIn.IsSignedIn);
        Assert.Equal("未サインイン", viewModel.SignIn.AccountText);
        Assert.Empty(teamSelection.Teams);
        Assert.Null(teamSelection.SelectedTeam);
        Assert.Equal("サインアウトしました", viewModel.StatusText);
        Assert.False(viewModel.IsStatusError);
    }

    [Fact]
    public async Task MainWindow_SignOut_失敗時は未サインイン状態にならずチーム選択も保持される()
    {
        FakeTeamsGateway gateway = new() { OwnedTeams = [new TeamInfo("team-1", "開発", null)] };
        RecordingNotificationService notifications = new();
        FakeDialogs dialogs = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        MemberFileViewModel memberFile = new(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs, gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        FakeAuthenticationService auth = new()
        {
            SignOutException = new InvalidOperationException("MSALのアカウント削除に失敗しました")
        };
        MainWindowViewModel viewModel = new(auth, gateway, notifications,
            preferences, teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new FakeManualService(), dialogs));
        await teamSelection.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        teamSelection.SelectedTeam = teamSelection.Teams[0];
        viewModel.SignIn.IsSignedIn = true;
        viewModel.SignIn.AccountText = "Current User (current@example.com)";

        await viewModel.SignIn.SignOutCommand.ExecuteAsync(null);

        Assert.True(viewModel.SignIn.IsSignedIn);
        Assert.Equal("Current User (current@example.com)", viewModel.SignIn.AccountText);
        Assert.Single(teamSelection.Teams);
        Assert.NotNull(teamSelection.SelectedTeam);
        Assert.Equal("MSALのアカウント削除に失敗しました", notifications.ErrorMessage);
        Assert.True(viewModel.IsStatusError);
        Assert.True(viewModel.SignIn.SignOutCommand.CanExecute(null));
    }

    [Fact]
    public async Task MainWindow_SignOut_タイムアウト時は未サインイン状態にならずエラーを通知する()
    {
        FakeTeamsGateway gateway = new() { OwnedTeams = [new TeamInfo("team-1", "開発", null)] };
        FakeDialogs dialogs = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        MemberFileViewModel memberFile = new(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs, gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        FakeAuthenticationService auth = new() { SignOutException = new TaskCanceledException("request timeout") };
        RecordingNotificationService notifications = new();
        MainWindowViewModel viewModel = new(auth, gateway, notifications,
            preferences, teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new FakeManualService(), dialogs));
        await teamSelection.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        teamSelection.SelectedTeam = teamSelection.Teams[0];
        viewModel.SignIn.IsSignedIn = true;
        viewModel.SignIn.AccountText = "Current User (current@example.com)";

        await viewModel.SignIn.SignOutCommand.ExecuteAsync(null);

        Assert.True(viewModel.SignIn.IsSignedIn);
        Assert.Equal("Current User (current@example.com)", viewModel.SignIn.AccountText);
        Assert.Single(teamSelection.Teams);
        Assert.NotNull(teamSelection.SelectedTeam);
        Assert.Equal("エラーが発生しました。通知の「詳細をコピー」から内容を確認できます", viewModel.StatusText);
        Assert.True(viewModel.IsStatusError);
        Assert.Equal("request timeout", notifications.ErrorMessage);
        Assert.True(viewModel.SignIn.SignOutCommand.CanExecute(null));
    }

    [Fact]
    public async Task MainWindow_手順の状態はサインイン_チーム選択_入力_差分確認の順に進む()
    {
        FakeTeamsGateway gateway = new() { OwnedTeams = [new TeamInfo("team-1", "開発", null)] };
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        FakeDialogs dialogs = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        FakeMemberListReader reader = new(new MemberListDocument(["new@example.com"], "members.csv",
            "C:\\members.csv", new DateTime(2026, 7, 28), "CSV", "email"));
        MemberFileViewModel memberFile = new(reader, new MemberTextParser(), preferences, dialogs, dialogs,
            gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        MainWindowViewModel viewModel = new(new FakeAuthenticationService(), gateway, dialogs,
            preferences, teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new FakeManualService(), dialogs));

        Assert.Equal(WorkflowStepState.Upcoming, viewModel.WorkflowSteps.Step1State);

        // MemberFileViewModel.LoadはTask.Runで非同期化されているため、テスト環境で
        // CollectionViewのクロススレッド例外を避けるにはDispatcherSynchronizationContextと
        // メッセージポンプ(RunOnDispatcher)が必要
        SynchronizationContext? originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            viewModel.SignIn.IsSignedIn = true;
            DispatcherTestHelper.RunOnDispatcher(() =>
                teamSelection.InitializeAsync("current-user", TestContext.Current.CancellationToken));
            Assert.Equal(WorkflowStepState.Current, viewModel.WorkflowSteps.Step1State);
            Assert.Equal(WorkflowStepState.Upcoming, viewModel.WorkflowSteps.Step2State);

            teamSelection.SelectedTeam = teamSelection.Teams[0];
            Assert.Equal(WorkflowStepState.Completed, viewModel.WorkflowSteps.Step1State);
            Assert.Equal(WorkflowStepState.Current, viewModel.WorkflowSteps.Step2State);
            Assert.Equal(WorkflowStepState.Upcoming, viewModel.WorkflowSteps.Step3State);

            DispatcherTestHelper.RunOnDispatcher(() =>
                memberFile.LoadDroppedFileCommand.ExecuteAsync("C:\\members.csv"));
            Assert.Equal(WorkflowStepState.Completed, viewModel.WorkflowSteps.Step2State);
            Assert.Equal(WorkflowStepState.Current, viewModel.WorkflowSteps.Step3State);

            DispatcherTestHelper.RunOnDispatcher(() => syncWorkspace.PreviewCommand.ExecuteAsync(null));
            Assert.Equal(WorkflowStepState.Completed, viewModel.WorkflowSteps.Step3State);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task MainWindow_現在メンバー取り込み中は競合する画面操作を無効化する()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeTeamsGateway gateway = new()
        {
            OnGetMembers = async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
        };
        FakeDialogs dialogs = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, dialogs);
        MemberFileViewModel memberFile = new(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs, gateway, dialogs, dialogs);
        SyncWorkspaceViewModel syncWorkspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), dialogs, dialogs);
        MainWindowViewModel viewModel = new(new FakeAuthenticationService(), gateway, dialogs,
            preferences, teamSelection, memberFile, syncWorkspace,
            new ManualViewModel(new FakeManualService(), dialogs));
        viewModel.SignIn.IsSignedIn = true;
        teamSelection.SelectedTeam = new TeamInfo("team-1", "開発", null);
        memberFile.SelectedInputIndex = 1;
        memberFile.PastedText = "old@example.com";

        Task importing = memberFile.Import.ImportCurrentMembersCommand.ExecuteAsync(null);
        await started.Task;

        Assert.False(viewModel.InputsEnabled);
        Assert.False(teamSelection.IsSelectionEnabled);
        Assert.False(teamSelection.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.SignIn.SignOutCommand.CanExecute(null));
        Assert.False(memberFile.ApplyPastedTextInputCommand.CanExecute(null));
        Assert.False(memberFile.LoadDroppedFileCommand.CanExecute("C:\\members.csv"));

        teamSelection.SelectedTeam = new TeamInfo("team-2", "別チーム", null);
        await importing;

        Assert.True(viewModel.InputsEnabled);
        Assert.True(teamSelection.IsSelectionEnabled);
        Assert.True(viewModel.SignIn.SignOutCommand.CanExecute(null));
        Assert.True(memberFile.ApplyPastedTextInputCommand.CanExecute(null));
    }
}