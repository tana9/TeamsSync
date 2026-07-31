using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class SyncWorkspaceViewModelTests
{
    [Fact]
    public async Task SyncWorkspace_確認ダイアログ失敗を通知して例外をUIへ伝播しない()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New", "new@example.com", "new@example.com");
        var notifications = new RecordingNotificationService();
        var confirmation = new FakeDialogs
        {
            OnConfirm = _ => throw new InvalidOperationException("dialog failed")
        };
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), confirmation, notifications);
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
                new DateTime(2026, 8, 1), "CSV", "email"), true);
        await viewModel.PreviewCommand.ExecuteAsync(null);

        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        Assert.Equal("同期を実行できませんでした", notifications.ErrorTitle);
        Assert.Equal("dialog failed", notifications.ErrorMessage);
    }

    [Fact]
    public async Task SyncWorkspace_結果保存失敗を通知して例外を伝播しない()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New", "new@example.com", "new@example.com");
        var writer = new FakeResultWriter { Exception = new IOException("disk full") };
        var picker = new FakeDialogs { ResultPath = "C:\\result.csv" };
        var notifications = new RecordingNotificationService();
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway), writer,
            new FakePreferences(), picker, new FakeDialogs(), notifications);
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
                new DateTime(2026, 7, 28), "CSV", "email"), true);
        await viewModel.PreviewCommand.ExecuteAsync(null);
        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        viewModel.SaveResultCommand.Execute(null);

        Assert.Equal("結果を保存できませんでした", notifications.ErrorTitle);
        Assert.Equal("disk full", notifications.ErrorMessage);
    }

    [Fact]
    public async Task SyncWorkspace_状態に応じて同期できない理由とフィルター件数を更新する()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["missing@example.com"] = new DirectoryUser(
            "user-1", "User", "missing@example.com", "missing@example.com");
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        Assert.Contains("サインイン", viewModel.SyncUnavailableReason);
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["missing@example.com"], "members.csv", "C:\\members.csv",
                DateTime.Now, "CSV", "email"), true);
        Assert.Contains("差分を確認", viewModel.SyncUnavailableReason);

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.Contains("同期を実行できます", viewModel.SyncUnavailableReason);
        Assert.Contains(viewModel.Filters, filter => filter.Kind == ChangeKind.Add && filter.Count == 1);
        Assert.Contains("列: email", viewModel.InputSummary);

        // メンバーリストの再指定で差分が無効化されたときは、古い件数を0件と表示するのではなく
        // 件数表示自体を消す(-1)。0件表示だと「確認した結果0件だった」と誤解されるおそれがあるため。
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["missing@example.com", "other@example.com"], "members2.csv",
                "C:\\members2.csv", DateTime.Now, "CSV", "email"), true);
        Assert.All(viewModel.Filters, filter => Assert.Equal(-1, filter.Count));
    }

    [Fact]
    public async Task SyncWorkspace_選択中フィルターの件数が変わるとDisplayTextの変更通知が発火する()
    {
        // ComboBoxのSelectedItem表示はDisplayMemberPathバインディング経由でINotifyPropertyChangedを
        // 見て更新される。CollectionViewSource.Refresh()だけではドロップダウン内のリストは
        // 更新されても選択中アイテムの表示テキストが更新されないWPFの既知の癖があるため、
        // 選択中インスタンスへ直接PropertyChangedが飛ぶことを確認する。
        var gateway = new FakeTeamsGateway();
        gateway.Users["missing@example.com"] = new DirectoryUser(
            "user-1", "User", "missing@example.com", "missing@example.com");
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["missing@example.com"], "members.csv", "C:\\members.csv",
                DateTime.Now, "CSV", "email"), true);

        var selectedFilter = viewModel.SelectedFilter;
        var raisedProperties = new List<string?>();
        selectedFilter.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.Same(selectedFilter, viewModel.SelectedFilter);
        Assert.Contains(nameof(ChangeFilter.DisplayText), raisedProperties);
        Assert.Equal("変更あり (1)", selectedFilter.DisplayText);
    }

    [Fact]
    public async Task SyncWorkspace_PreviewBuildsChangesAndUpdatesCommandState()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        var team = new TeamInfo("team-1", "開発", null);
        var document = new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");

        viewModel.SetContext(team, document, true);
        Assert.True(viewModel.PreviewCommand.CanExecute(null));
        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Changes);
        Assert.Equal(ChangeKind.Add, viewModel.Changes[0].Kind);
        Assert.Contains("追加 1", viewModel.SummaryText);
        Assert.True(viewModel.ExecuteSyncCommand.CanExecute(null));
    }

    [Fact]
    public async Task SyncWorkspace_指定メンバー削除では入力した一般メンバーだけを削除する()
    {
        var gateway = new FakeTeamsGateway
        {
            Members =
            [
                new TeamMember("target-membership", "target-user", "Target", "target@example.com", false),
                new TeamMember("other-membership", "other-user", "Other", "other@example.com", false),
                new TeamMember("owner-membership", "owner-user", "Owner", "owner@example.com", true)
            ]
        };
        gateway.OnRemove = (_, membershipId, _) =>
        {
            gateway.Members = gateway.Members
                .Where(member => member.MembershipId != membershipId).ToList();
            return Task.CompletedTask;
        };
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        viewModel.IsRemoveSpecifiedSelected = true;
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["target@example.com", "owner@example.com"], "members.csv",
                "C:\\members.csv", DateTime.Now, "CSV", "email"), true);

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsRemoveSpecifiedSelected);
        Assert.Single(viewModel.Changes, change => change.Kind == ChangeKind.Remove);
        Assert.Single(viewModel.Changes, change => change.Kind == ChangeKind.Protected);
        Assert.DoesNotContain(viewModel.Changes, change => change.Change.UserId == "other-user");
        Assert.Contains("指定した一般メンバー", viewModel.RemovalWarningMessage);

        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        Assert.Equal([("team-1", "target-membership")], gateway.Removed);
        Assert.Empty(gateway.Added);
        Assert.Contains(gateway.Members, member => member.UserId == "other-user");
        Assert.Contains(gateway.Members, member => member.UserId == "owner-user");
    }

    [Fact]
    public async Task SyncWorkspace_確認済みの差分がある状態で同期モードを切り替えるとクリアを通知する()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        var team = new TeamInfo("team-1", "開発", null);
        var document = new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        viewModel.SetContext(team, document, true);
        await viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.Single(viewModel.Changes);
        var notifications = new List<string>();
        viewModel.StatusChanged += (message, _) => notifications.Add(message);

        viewModel.IsFullSyncSelected = true;

        Assert.Empty(viewModel.Changes);
        Assert.Contains(notifications, message => message.Contains("クリア"));
    }

    [Fact]
    public void SyncWorkspace_差分未確認の状態で同期モードを切り替えても通知しない()
    {
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(new FakeTeamsGateway()),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        var notifications = new List<string>();
        viewModel.StatusChanged += (message, _) => notifications.Add(message);

        viewModel.IsFullSyncSelected = true;

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task SyncWorkspace_WhenMembershipIsUnchanged_ExecutesRevalidatedPlan()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        var document = new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        viewModel.SetContext(new TeamInfo("team-1", "開発", null), document, true);
        await viewModel.PreviewCommand.ExecuteAsync(null);

        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        Assert.Equal([("team-1", "new-user")], gateway.Added);
    }

    [Fact]
    public async Task SyncWorkspace_WhenMembershipChangesAfterConfirmation_ShowsLatestPlanWithoutWriting()
    {
        var gateway = new FakeTeamsGateway
        {
            Members = [new TeamMember("old-membership", "old-user", "Old", "old@example.com", false)]
        };
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        var dialogs = new FakeDialogs
        {
            OnConfirm = _ =>
            {
                gateway.Members =
                    [new TeamMember("old-membership", "old-user", "Old", "old@example.com", true)];
                return true;
            }
        };
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), dialogs, dialogs, dialogs);
        viewModel.SelectedMode = viewModel.Modes.Single(mode => mode.Mode == SyncMode.FullSync);
        var team = new TeamInfo("team-1", "開発", null);
        var document = new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        viewModel.SetContext(team, document, true);
        await viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.Contains(viewModel.Changes, change => change.Kind == ChangeKind.Remove);

        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        Assert.Empty(gateway.Added);
        Assert.Empty(gateway.Removed);
        Assert.DoesNotContain(viewModel.Changes, change => change.Kind == ChangeKind.Remove);
        Assert.Contains("変更", dialogs.WarningTitle);
        Assert.True(viewModel.ExecuteSyncCommand.CanExecute(null));
    }

    [Fact]
    public async Task SyncWorkspace_失敗後に実状態の残差だけを再実行する()
    {
        var gateway = new FakeTeamsGateway
        {
            Members =
            [
                new TeamMember("old-membership", "old-user", "Old", "old@example.com", false),
                new TeamMember("new-membership", "new-user", "New", "new@example.com", false)
            ]
        };
        gateway.OnRemove = (_, _, _) => Task.FromException(new InvalidOperationException("remove failed"));
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        viewModel.SelectedMode = viewModel.Modes.Single(mode => mode.Mode == SyncMode.FullSync);
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
                new DateTime(2026, 7, 28), "CSV", "email"), true);
        await viewModel.PreviewCommand.ExecuteAsync(null);

        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        Assert.DoesNotContain(viewModel.Changes, change => change.Kind == ChangeKind.Add);
        Assert.Single(viewModel.Changes, change => change.Kind == ChangeKind.Remove);
        Assert.True(viewModel.ExecuteSyncCommand.CanExecute(null));

        gateway.OnRemove = (_, membershipId, _) =>
        {
            gateway.Members = gateway.Members
                .Where(member => member.MembershipId != membershipId).ToList();
            return Task.CompletedTask;
        };
        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        Assert.Empty(gateway.Added);
        Assert.Equal(2, gateway.Removed.Count);
        Assert.False(viewModel.ExecuteSyncCommand.CanExecute(null));
    }

    [Fact]
    public async Task SyncWorkspace_表示フィルターを変更すると差分一覧が絞り込まれる()
    {
        var gateway = new FakeTeamsGateway
        {
            Members =
            [
                new TeamMember("old-membership", "old-user", "Old", "old@example.com", false),
                new TeamMember("keep-membership", "keep-user", "Keep", "keep@example.com", false)
            ]
        };
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        viewModel.SelectedMode = viewModel.Modes.Single(mode => mode.Mode == SyncMode.FullSync);
        var document = new MemberListDocument(["new@example.com", "keep@example.com"], "members.csv",
            "C:\\members.csv", new DateTime(2026, 7, 28), "CSV", "email");
        viewModel.SetContext(new TeamInfo("team-1", "開発", null), document, true);
        await viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.Contains(viewModel.Changes, change => change.Kind == ChangeKind.Add);
        Assert.Contains(viewModel.Changes, change => change.Kind == ChangeKind.Remove);
        Assert.Contains(viewModel.Changes, change => change.Kind == ChangeKind.Keep);

        viewModel.SelectedFilter = viewModel.Filters.Single(filter => filter.Kind == ChangeKind.Add);

        var filtered = viewModel.ChangesView.Cast<SyncChangeRowViewModel>().ToList();
        Assert.Single(filtered);
        Assert.Equal(ChangeKind.Add, filtered[0].Kind);
    }

    [Fact]
    public async Task SyncWorkspace_同期完了後はHasSyncResultと成功件数を公開する()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        // FakeTeamsGateway.AddMemberAsync既定ではMembersを更新しないため、再検証(ReconcileAsync)後も
        // 追加前の状態のまま扱われ未反映として残ってしまう。実際のGraph APIと同様に反映されたことにするため、
        // OnAddでMembersへ反映する。
        gateway.OnAdd = (_, userId, _) =>
        {
            gateway.Members = gateway.Members
                .Append(new TeamMember("new-membership", userId, "New User", "new@example.com", false)).ToList();
            return Task.CompletedTask;
        };
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
                new DateTime(2026, 7, 28), "CSV", "email"), true);
        Assert.False(viewModel.HasSyncResult);

        await viewModel.PreviewCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasPlan);

        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSyncResult);
        Assert.Equal(1, viewModel.ResultSuccessCount);
        Assert.Equal(0, viewModel.ResultFailureCount);
        Assert.False(viewModel.ResultCancelled);
        Assert.False(viewModel.HasFailedResults);
        Assert.Equal(0, viewModel.ResultRemainingCount);
    }

    [Fact]
    public async Task SyncWorkspace_一部失敗時は失敗した操作の一覧を原因つきで公開する()
    {
        var gateway = new FakeTeamsGateway
        {
            Members =
            [
                new TeamMember("old-membership", "old-user", "Old", "old@example.com", false),
                new TeamMember("new-membership", "new-user", "New", "new@example.com", false)
            ]
        };
        gateway.OnRemove = (_, _, _) => Task.FromException(new InvalidOperationException("remove failed"));
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        viewModel.SelectedMode = viewModel.Modes.Single(mode => mode.Mode == SyncMode.FullSync);
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
                new DateTime(2026, 7, 28), "CSV", "email"), true);
        await viewModel.PreviewCommand.ExecuteAsync(null);

        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.ResultFailureCount);
        Assert.True(viewModel.HasFailedResults);
        var failed = Assert.Single(viewModel.FailedResults);
        Assert.Equal("削除", failed.KindLabel);
        Assert.Equal("old@example.com", failed.Email);
        Assert.Equal("remove failed", failed.Error);
    }

    [Fact]
    public async Task SyncWorkspace_新しい入力に切り替えると実行結果もクリアする()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        var document = new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        viewModel.SetContext(new TeamInfo("team-1", "開発", null), document, true);
        await viewModel.PreviewCommand.ExecuteAsync(null);
        await viewModel.ExecuteSyncCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasSyncResult);

        viewModel.SetContext(new TeamInfo("team-2", "別チーム", null), document, true);

        Assert.False(viewModel.HasSyncResult);
        Assert.Equal(0, viewModel.ResultSuccessCount);
        Assert.Empty(viewModel.FailedResults);
    }

    [Fact]
    public async Task SyncWorkspace_差分確認のたびにDiffFocusRequestedを1回通知する()
    {
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeDialogs());
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
                new DateTime(2026, 7, 28), "CSV", "email"), true);
        var focusRequests = 0;
        viewModel.DiffFocusRequested += () => focusRequests++;

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.Equal(1, focusRequests);
    }

    [Fact]
    public async Task SyncWorkspace_終了要求で実行中の操作をキャンセルして完了を待つ()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeTeamsGateway();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New", "new@example.com", "new@example.com");
        gateway.OnAdd = async (_, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        };
        var dialogs = new FakeDialogs();
        var viewModel = new SyncWorkspaceViewModel(new TeamSyncService(gateway),
            new FakeResultWriter(), new FakePreferences(), dialogs, dialogs, dialogs);
        viewModel.SetContext(new TeamInfo("team-1", "開発", null),
            new MemberListDocument(["new@example.com"], "members.csv", "C:\\members.csv",
                new DateTime(2026, 7, 28), "CSV", "email"), true);
        await viewModel.PreviewCommand.ExecuteAsync(null);
        var execution = viewModel.ExecuteSyncCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        await viewModel.CancelAndWaitAsync();

        Assert.True(execution.IsCompleted);
        Assert.False(viewModel.IsSyncing);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }
}
