using System.Windows.Threading;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Files;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void MainWindow_IsNotSignedIn_初期状態でtrueでありサインイン後にfalseになる()
    {
        var gateway = new FakeTeamsGateway();
        var dialogs = new FakeDialogs();
        var preferences = new FakePreferences();
        var teamSelection = new TeamSelectionViewModel(gateway, dialogs);
        var memberFile = new MemberFileViewModel(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs);
        var syncWorkspace = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), preferences, dialogs, dialogs, dialogs);
        var viewModel = new MainWindowViewModel(new FakeAuthenticationService(), gateway, dialogs,
            new FakeManualService(), preferences, teamSelection, memberFile, syncWorkspace);

        Assert.True(viewModel.IsNotSignedIn);

        viewModel.IsSignedIn = true;

        Assert.False(viewModel.IsNotSignedIn);
    }

    [Fact]
    public void MainWindow_OpenManual_成功時はエラーダイアログを表示しない()
    {
        var gateway = new FakeTeamsGateway();
        var dialogs = new FakeDialogs();
        var preferences = new FakePreferences();
        var teamSelection = new TeamSelectionViewModel(gateway, dialogs);
        var memberFile = new MemberFileViewModel(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs);
        var syncWorkspace = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), preferences, dialogs, dialogs, dialogs);
        var viewModel = new MainWindowViewModel(new FakeAuthenticationService(), gateway, dialogs,
            new FakeManualService(), preferences, teamSelection, memberFile, syncWorkspace);

        viewModel.OpenManualCommand.Execute(null);
    }

    [Fact]
    public void MainWindow_OpenManual_失敗時はエラーダイアログを表示する()
    {
        var gateway = new FakeTeamsGateway();
        var dialogs = new FakeDialogs();
        var notifications = new RecordingNotificationService();
        var preferences = new FakePreferences();
        var teamSelection = new TeamSelectionViewModel(gateway, dialogs);
        var memberFile = new MemberFileViewModel(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs);
        var syncWorkspace = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), preferences, dialogs, dialogs, dialogs);
        var viewModel = new MainWindowViewModel(new FakeAuthenticationService(), gateway, notifications,
            new ThrowingManualService(), preferences, teamSelection, memberFile, syncWorkspace);

        viewModel.OpenManualCommand.Execute(null);

        Assert.Equal("展開に失敗しました", notifications.ErrorMessage);
        Assert.Equal("マニュアルを開けませんでした", notifications.ErrorTitle);
    }

    [Fact]
    public async Task MainWindow_SignOut_成功時は未サインイン状態になりチーム選択もクリアされる()
    {
        var gateway = new FakeTeamsGateway
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null)]
        };
        var dialogs = new FakeDialogs();
        var preferences = new FakePreferences();
        var teamSelection = new TeamSelectionViewModel(gateway, dialogs);
        var memberFile = new MemberFileViewModel(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs);
        var syncWorkspace = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), preferences, dialogs, dialogs, dialogs);
        var viewModel = new MainWindowViewModel(new FakeAuthenticationService(), gateway, dialogs,
            new FakeManualService(), preferences, teamSelection, memberFile, syncWorkspace);
        await teamSelection.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        teamSelection.SelectedTeam = teamSelection.Teams[0];
        viewModel.IsSignedIn = true;
        viewModel.AccountText = "Current User (current@example.com)";

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSignedIn);
        Assert.Equal("未サインイン", viewModel.AccountText);
        Assert.Empty(teamSelection.Teams);
        Assert.Null(teamSelection.SelectedTeam);
        Assert.Equal("サインアウトしました", viewModel.StatusText);
        Assert.False(viewModel.IsStatusError);
    }

    [Fact]
    public async Task MainWindow_SignOut_失敗時は未サインイン状態にならずチーム選択も保持される()
    {
        var gateway = new FakeTeamsGateway
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null)]
        };
        var notifications = new RecordingNotificationService();
        var dialogs = new FakeDialogs();
        var preferences = new FakePreferences();
        var teamSelection = new TeamSelectionViewModel(gateway, dialogs);
        var memberFile = new MemberFileViewModel(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs);
        var syncWorkspace = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), preferences, dialogs, dialogs, dialogs);
        var auth = new FakeAuthenticationService
        {
            SignOutException = new InvalidOperationException("MSALのアカウント削除に失敗しました")
        };
        var viewModel = new MainWindowViewModel(auth, gateway, notifications,
            new FakeManualService(), preferences, teamSelection, memberFile, syncWorkspace);
        await teamSelection.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        teamSelection.SelectedTeam = teamSelection.Teams[0];
        viewModel.IsSignedIn = true;
        viewModel.AccountText = "Current User (current@example.com)";

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSignedIn);
        Assert.Equal("Current User (current@example.com)", viewModel.AccountText);
        Assert.Single(teamSelection.Teams);
        Assert.NotNull(teamSelection.SelectedTeam);
        Assert.Equal("MSALのアカウント削除に失敗しました", notifications.ErrorMessage);
        Assert.True(viewModel.IsStatusError);
        Assert.True(viewModel.SignOutCommand.CanExecute(null));
    }

    [Fact]
    public async Task MainWindow_SignOut_キャンセル時は未サインイン状態にならずチーム選択も保持される()
    {
        var gateway = new FakeTeamsGateway
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null)]
        };
        var dialogs = new FakeDialogs();
        var preferences = new FakePreferences();
        var teamSelection = new TeamSelectionViewModel(gateway, dialogs);
        var memberFile = new MemberFileViewModel(new FakeMemberListReader(null!), new MemberTextParser(),
            preferences, dialogs, dialogs);
        var syncWorkspace = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), preferences, dialogs, dialogs, dialogs);
        var auth = new FakeAuthenticationService { SignOutException = new OperationCanceledException() };
        var viewModel = new MainWindowViewModel(auth, gateway, dialogs,
            new FakeManualService(), preferences, teamSelection, memberFile, syncWorkspace);
        await teamSelection.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        teamSelection.SelectedTeam = teamSelection.Teams[0];
        viewModel.IsSignedIn = true;
        viewModel.AccountText = "Current User (current@example.com)";

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSignedIn);
        Assert.Equal("Current User (current@example.com)", viewModel.AccountText);
        Assert.Single(teamSelection.Teams);
        Assert.NotNull(teamSelection.SelectedTeam);
        Assert.Equal("処理を中止しました", viewModel.StatusText);
        Assert.False(viewModel.IsStatusError);
        Assert.True(viewModel.SignOutCommand.CanExecute(null));
    }

    [Fact]
    public async Task MainWindow_手順の状態はサインイン_チーム選択_入力_差分確認の順に進む()
    {
        var gateway = new FakeTeamsGateway
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null)]
        };
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        var dialogs = new FakeDialogs();
        var preferences = new FakePreferences();
        var teamSelection = new TeamSelectionViewModel(gateway, dialogs);
        var reader = new FakeMemberListReader(new MemberListDocument(["new@example.com"], "members.csv",
            "C:\\members.csv", new DateTime(2026, 7, 28), "CSV", "email"));
        var memberFile = new MemberFileViewModel(reader, new MemberTextParser(), preferences, dialogs, dialogs);
        var syncWorkspace = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), preferences, dialogs, dialogs, dialogs);
        var viewModel = new MainWindowViewModel(new FakeAuthenticationService(), gateway, dialogs,
            new FakeManualService(), preferences, teamSelection, memberFile, syncWorkspace);

        Assert.Equal(WorkflowStepState.Upcoming, viewModel.Step1State);

        // MemberFileViewModel.LoadはTask.Runで非同期化されているため、テスト環境で
        // CollectionViewのクロススレッド例外を避けるにはDispatcherSynchronizationContextと
        // メッセージポンプ(RunOnDispatcher)が必要
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            viewModel.IsSignedIn = true;
            DispatcherTestHelper.RunOnDispatcher(() =>
                teamSelection.InitializeAsync("current-user", TestContext.Current.CancellationToken));
            Assert.Equal(WorkflowStepState.Current, viewModel.Step1State);
            Assert.Equal(WorkflowStepState.Upcoming, viewModel.Step2State);

            teamSelection.SelectedTeam = teamSelection.Teams[0];
            Assert.Equal(WorkflowStepState.Completed, viewModel.Step1State);
            Assert.Equal(WorkflowStepState.Current, viewModel.Step2State);
            Assert.Equal(WorkflowStepState.Upcoming, viewModel.Step3State);

            DispatcherTestHelper.RunOnDispatcher(() => memberFile.LoadDroppedFileCommand.ExecuteAsync("C:\\members.csv"));
            Assert.Equal(WorkflowStepState.Completed, viewModel.Step2State);
            Assert.Equal(WorkflowStepState.Current, viewModel.Step3State);

            DispatcherTestHelper.RunOnDispatcher(() => syncWorkspace.PreviewCommand.ExecuteAsync(null));
            Assert.Equal(WorkflowStepState.Completed, viewModel.Step3State);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }
}
