using System.Windows.Threading;

using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Files;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class MemberFileViewModelTests
{
    [Fact]
    public async Task MemberFile_ファイル読取結果を1行1件でテキストへコピーする()
    {
        MemberListDocument fileDocument = new(["a@example.com", "山田 太郎"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 8, 2), "CSV", "メールアドレス");
        FakeDialogs dialogs = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(fileDocument), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, new FakeTeamsGateway(), dialogs);
        await viewModel.LoadDroppedFileCommand.ExecuteAsync("C:\\members.csv");
        Assert.True(viewModel.CopyFileContentToTextCommand.CanExecute(null));

        await viewModel.CopyFileContentToTextCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.SelectedInputIndex);
        Assert.Equal($"a@example.com{Environment.NewLine}山田 太郎", viewModel.PastedText);
        Assert.Null(viewModel.Document);
        Assert.Contains("入力を反映", viewModel.PasteInfoText);
        Assert.Contains("元ファイルは変更されません", viewModel.PasteInfoText);
        Assert.Equal(0, dialogs.FileContentConfirmationCount);
    }

    [Fact]
    public async Task MemberFile_既存テキストの置き換えを拒否すると入力とファイル文書を維持する()
    {
        MemberListDocument fileDocument = new(["new@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 8, 2), "CSV", "メールアドレス");
        FakeDialogs dialogs = new() { OnConfirmReplaceTextWithFileContent = (_, _) => false };
        MemberFileViewModel viewModel = new(new FakeMemberListReader(fileDocument), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, new FakeTeamsGateway(), dialogs)
        {
            PastedText = "old@example.com"
        };
        await viewModel.LoadDroppedFileCommand.ExecuteAsync("C:\\members.csv");

        await viewModel.CopyFileContentToTextCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.SelectedInputIndex);
        Assert.Equal("old@example.com", viewModel.PastedText);
        Assert.Same(fileDocument, viewModel.Document);
        Assert.Equal(1, dialogs.FileContentConfirmationCount);
    }

    [Fact]
    public async Task MemberFile_コピーした内容を編集して反映できる()
    {
        MemberListDocument fileDocument = new(["a@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 8, 2), "CSV", "メールアドレス");
        FakeDialogs dialogs = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(fileDocument), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, new FakeTeamsGateway(), dialogs);
        await viewModel.LoadDroppedFileCommand.ExecuteAsync("C:\\members.csv");
        await viewModel.CopyFileContentToTextCommand.ExecuteAsync(null);
        viewModel.PastedText += $"{Environment.NewLine}b@example.com";

        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);

        Assert.Equal(["a@example.com", "b@example.com"], viewModel.Document!.Addresses);
        Assert.Contains("2件", viewModel.PasteInfoText);
    }

    [Fact]
    public async Task MemberFile_設定保存失敗でも読込済み文書を維持する()
    {
        MemberListDocument document = new(["user@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        FakePreferences preferences = new() { SaveException = new IOException("disk full") };
        FakeFilePickerService picker = new();
        RecordingNotificationService notifications = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(document), new MemberTextParser(),
            preferences, picker, notifications, new FakeTeamsGateway(), new FakeDialogs());

        await viewModel.LoadDroppedFileCommand.ExecuteAsync(document.FullPath);

        Assert.Same(document, viewModel.Document);
        Assert.Contains("設定を保存できません", notifications.WarningTitle);
        Assert.Null(notifications.ErrorMessage);
        Assert.Equal(@"C:\", preferences.LastFolder);

        await viewModel.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\", picker.InitialDirectory);
    }

    [Fact]
    public async Task MemberFile_LoadsDroppedFileAndDisablesCommandsWhileUnavailable()
    {
        MemberListDocument document = new(["user@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        FakeMemberListReader reader = new(document);
        MemberFileViewModel viewModel = new(reader, new MemberTextParser(), new FakePreferences(),
            new FakeDialogs(), new FakeDialogs(), new FakeTeamsGateway(), new FakeDialogs());
        bool changed = false;
        viewModel.DocumentChanged += () => changed = true;

        await viewModel.LoadDroppedFileCommand.ExecuteAsync(document.FullPath);

        Assert.Same(document, viewModel.Document);
        Assert.True(changed);
        Assert.Contains("1件", viewModel.FileInfoText);
        viewModel.SetEnabled(false);
        Assert.False(viewModel.BrowseCommand.CanExecute(null));
        Assert.False(viewModel.LoadDroppedFileCommand.CanExecute(document.FullPath));
    }

    [Fact]
    public async Task MemberFile_新しいファイルの読込失敗時は以前の文書を無効化する()
    {
        MemberListDocument first = new(["old@example.com"], "old.csv", "C:\\old.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        SwitchingMemberListReader reader = new(first);
        RecordingNotificationService notifications = new();
        MemberFileViewModel viewModel = new(reader, new MemberTextParser(), new FakePreferences(),
            new FakeDialogs(), notifications, new FakeTeamsGateway(), new FakeDialogs());
        int changedCount = 0;
        viewModel.DocumentChanged += () => changedCount++;

        await viewModel.LoadDroppedFileCommand.ExecuteAsync(first.FullPath);
        Assert.Same(first, viewModel.Document);
        reader.Exception = new InvalidDataException("壊れたファイルです");

        await viewModel.LoadDroppedFileCommand.ExecuteAsync("C:\\broken.csv");

        Assert.Null(viewModel.Document);
        Assert.Equal("C:\\broken.csv", viewModel.FilePath);
        Assert.Contains("読込に失敗", viewModel.FileInfoText);
        Assert.Equal(2, changedCount);
        Assert.Equal("壊れたファイルです", notifications.ErrorMessage);
    }

    [Fact]
    public async Task MemberFile_読込失敗後は以前の同期差分を実行できない()
    {
        MemberListDocument document = new(["new@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        SwitchingMemberListReader reader = new(document);
        FakeTeamsGateway gateway = new();
        gateway.Users["new@example.com"] =
            new DirectoryUser("new-user", "New User", "new@example.com", "new@example.com");
        RecordingNotificationService notifications = new();
        FakePreferences preferences = new();
        TeamSelectionViewModel teamSelection = new(gateway, notifications)
        {
            SelectedTeam = new TeamInfo("team-1", "開発", null)
        };
        MemberFileViewModel memberFile = new(reader, new MemberTextParser(), preferences,
            new FakeDialogs(), notifications, gateway, new FakeDialogs());
        SyncWorkspaceViewModel workspace = SyncWorkspaceViewModelFactory.Create(new SyncPlanService(gateway), new SyncExecutor(gateway),
            new FakeResultWriter(), new FakeDialogs(), notifications);
        MainWindowViewModel main = new(new FakeAuthenticationService(), gateway, notifications,
            preferences, teamSelection, memberFile, workspace,
            new ManualViewModel(new FakeManualService(), notifications));
        main.SignIn.IsSignedIn = true;

        // MemberFileViewModel.LoadはTask.Runでファイルを読み込むため、後続のDocumentChanged通知
        // (ObservableCollectionの変更を含む)はTask.Runの完了スレッド上で継続される。
        // WPF実行時はDispatcherSynchronizationContextにより自動的にUIスレッドへ戻るが、
        // テストにはDispatcherのメッセージポンプが存在しないため、DispatcherSynchronizationContextを
        // 設定した上でPushFrameによりメッセージポンプを模擬し、継続がテストスレッド上で実行されるようにする
        SynchronizationContext? originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            DispatcherTestHelper.RunOnDispatcher(() =>
                memberFile.LoadDroppedFileCommand.ExecuteAsync(document.FullPath));
            DispatcherTestHelper.RunOnDispatcher(() => workspace.PreviewCommand.ExecuteAsync(null));
            Assert.True(workspace.ExecuteSyncCommand.CanExecute(null));

            reader.Exception = new InvalidDataException("壊れたファイルです");
            DispatcherTestHelper.RunOnDispatcher(() =>
                memberFile.LoadDroppedFileCommand.ExecuteAsync("C:\\broken.csv"));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        Assert.False(workspace.ExecuteSyncCommand.CanExecute(null));
        Assert.Empty(workspace.Changes);
    }

    [Fact]
    public async Task MemberFile_貼り付け解析中を表示して完了後に解除する()
    {
        BlockingTextParser parser = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), parser,
            new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeTeamsGateway(), new FakeDialogs())
        {
            SelectedInputIndex = 1, PastedText = "user@example.com"
        };

        Task execution = viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);
        await parser.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsParsing);
        Assert.False(viewModel.ApplyPastedTextInputCommand.CanExecute(null));

        parser.Release.Set();
        await execution;

        Assert.False(viewModel.IsParsing);
        Assert.NotNull(viewModel.Document);
    }

    [Fact]
    public async Task MemberFile_貼り付け解析をキャンセルできる()
    {
        BlockingTextParser parser = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), parser,
            new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeTeamsGateway(), new FakeDialogs())
        {
            SelectedInputIndex = 1, PastedText = "user@example.com"
        };

        Task execution = viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);
        await parser.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        viewModel.CancelParsingCommand.Execute(null);
        await execution;

        Assert.False(viewModel.IsParsing);
        Assert.Null(viewModel.Document);
        Assert.Contains("キャンセル", viewModel.PasteInfoText);
    }

    [Fact]
    public async Task MemberFile_ファイル読込中を表示して完了後に解除する()
    {
        MemberListDocument document = new(["user@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        BlockingMemberListReader reader = new(document);
        MemberFileViewModel viewModel = new(reader, new MemberTextParser(),
            new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeTeamsGateway(), new FakeDialogs());

        Task execution = viewModel.LoadDroppedFileCommand.ExecuteAsync(document.FullPath);
        await reader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsLoadingFile);
        Assert.False(viewModel.BrowseCommand.CanExecute(null));
        Assert.False(viewModel.CopyFileContentToTextCommand.CanExecute(null));

        reader.Release.Set();
        await execution;

        Assert.False(viewModel.IsLoadingFile);
        Assert.Same(document, viewModel.Document);
        Assert.True(viewModel.CopyFileContentToTextCommand.CanExecute(null));
    }

    [Fact]
    public async Task MemberFile_ファイル読込をキャンセルすると以前の文書を維持する()
    {
        MemberListDocument next = new(["new@example.com"], "new.csv", "C:\\new.csv",
            new DateTime(2026, 7, 28), "CSV", "email");
        BlockingMemberListReader reader = new(next);
        MemberFileViewModel viewModel = new(reader, new MemberTextParser(),
            new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeTeamsGateway(), new FakeDialogs());

        // キャンセルされた読込ではDocumentを変更しないため、読込前の状態(未選択)が維持されることを確認する
        Task execution = viewModel.LoadDroppedFileCommand.ExecuteAsync(next.FullPath);
        await reader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.CancelLoadCommand.CanExecute(null));
        viewModel.CancelLoadCommand.Execute(null);
        reader.Release.Set();
        await execution;

        Assert.False(viewModel.IsLoadingFile);
        Assert.Null(viewModel.Document);
        Assert.Contains("キャンセル", viewModel.FileInfoText);
        Assert.True(viewModel.LoadDroppedFileCommand.CanExecute(next.FullPath));
    }

    [Fact]
    public async Task MemberFile_貼り付けエラーの行番号を表示してステータスへ通知する()
    {
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeTeamsGateway(), new FakeDialogs())
        {
            SelectedInputIndex = 1, PastedText = "user@example.com\ninvalid\tvalue"
        };
        string? status = null;
        viewModel.StatusChanged += (message, isError) => status = isError ? message : null;

        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Document);
        Assert.True(viewModel.IsPasteError);
        Assert.Contains("2行目", viewModel.PasteInfoText);
        Assert.Contains("2行目", status);
    }

    [Fact]
    public async Task MemberFile_貼り付け入力を解析し変更時に選択中の文書を無効化する()
    {
        MemberFileViewModel viewModel = new(
            new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeTeamsGateway(), new FakeDialogs());
        int changedCount = 0;
        viewModel.DocumentChanged += () => changedCount++;
        viewModel.SelectedInputIndex = 1;

        viewModel.PastedText = "user@example.com\r\nUSER@example.com\n山田 太郎";
        Assert.Null(viewModel.Document);
        Assert.True(viewModel.ApplyPastedTextInputCommand.CanExecute(null));
        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.Document);
        Assert.Equal(["user@example.com", "山田 太郎"], viewModel.Document.Addresses);
        Assert.Contains("重複1件", viewModel.PasteInfoText);

        viewModel.PastedText = " ";

        Assert.Null(viewModel.Document);
        Assert.True(changedCount >= 2);
    }

    [Fact]
    public async Task MemberFile_ファイル読込失敗時にInputFocusRequestedを通知する()
    {
        SwitchingMemberListReader reader = new(
            new MemberListDocument(["a@example.com"], "a.csv", "C:\\a.csv",
                new DateTime(2026, 7, 28), "CSV", "email")) { Exception = new InvalidDataException("壊れたファイルです") };
        MemberFileViewModel viewModel = new(reader, new MemberTextParser(), new FakePreferences(),
            new FakeDialogs(), new RecordingNotificationService(), new FakeTeamsGateway(), new FakeDialogs());
        bool focusRequested = false;
        viewModel.InputFocusRequested += () => focusRequested = true;

        // Loadは内部でTask.Runを使うため、Execute(fire-and-forget)ではなくExecuteAsyncで完了を待つ
        await viewModel.LoadDroppedFileCommand.ExecuteAsync("C:\\broken.csv");

        Assert.True(focusRequested);
    }

    [Fact]
    public async Task MemberFile_貼り付け解析失敗時にInputFocusRequestedを通知する()
    {
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), new FakeDialogs(), new FakeDialogs(), new FakeTeamsGateway(), new FakeDialogs())
        {
            SelectedInputIndex = 1, PastedText = "user@example.com\ninvalid\tvalue"
        };
        bool focusRequested = false;
        viewModel.InputFocusRequested += () => focusRequested = true;

        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);

        Assert.True(focusRequested);
    }

    [Fact]
    public async Task MemberFile_現在の一般メンバーだけをアドレス順で取り込む()
    {
        FakeTeamsGateway gateway = new()
        {
            Members =
            [
                new TeamMember("owner", "owner-id", "Owner", "owner@example.com", true),
                new TeamMember("b", "b-id", "B", "B@example.com", false),
                new TeamMember("a", "a-id", "A", "a@example.com", false),
                new TeamMember("a2", "a-id", "A", "A@example.com", false)
            ]
        };
        FakeDialogs dialogs = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, gateway, dialogs);
        viewModel.SetSelectedTeam(new TeamInfo("team-1", "開発", null));
        viewModel.SelectedInputIndex = 1;

        await viewModel.Import.ImportCurrentMembersCommand.ExecuteAsync(null);

        Assert.Equal(1, viewModel.SelectedInputIndex);
        Assert.Equal($"A <a@example.com>{Environment.NewLine}B <B@example.com>", viewModel.PastedText);
        Assert.Equal(["a@example.com", "B@example.com"], viewModel.Document!.Addresses);
        Assert.Equal("Teamsから取り込み: 開発", viewModel.Document.SourceName);
        Assert.DoesNotContain("owner@example.com", viewModel.PastedText);
        Assert.Equal(0, dialogs.ReplaceMemberInputConfirmationCount);
    }

    [Fact]
    public async Task MemberFile_既存入力の置き換えを拒否すると内容を維持する()
    {
        FakeTeamsGateway gateway = new()
        {
            Members = [new TeamMember("new", "new-id", "New", "new@example.com", false)]
        };
        FakeDialogs dialogs = new() { OnConfirmReplaceMemberInput = (_, _) => false };
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, gateway, dialogs)
        {
            SelectedInputIndex = 1, PastedText = "old@example.com"
        };
        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);
        MemberListDocument? originalDocument = viewModel.Document;
        viewModel.SetSelectedTeam(new TeamInfo("team-1", "開発", null));

        await viewModel.Import.ImportCurrentMembersCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.ReplaceMemberInputConfirmationCount);
        Assert.Equal("old@example.com", viewModel.PastedText);
        Assert.Same(originalDocument, viewModel.Document);
    }

    [Fact]
    public async Task MemberFile_一般メンバーが0人なら既存入力を維持する()
    {
        FakeTeamsGateway gateway = new()
        {
            Members = [new TeamMember("owner", "owner-id", "Owner", "owner@example.com", true)]
        };
        FakeDialogs dialogs = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, gateway, dialogs)
        {
            SelectedInputIndex = 1, PastedText = "old@example.com"
        };
        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);
        MemberListDocument? originalDocument = viewModel.Document;
        viewModel.SetSelectedTeam(new TeamInfo("team-1", "開発", null));

        await viewModel.Import.ImportCurrentMembersCommand.ExecuteAsync(null);

        Assert.Equal("old@example.com", viewModel.PastedText);
        Assert.Same(originalDocument, viewModel.Document);
        Assert.Contains("一般メンバー", dialogs.WarningTitle);
    }

    [Fact]
    public async Task MemberFile_現在メンバー取得のキャンセル時は既存入力を維持する()
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
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, gateway, dialogs)
        {
            SelectedInputIndex = 1, PastedText = "old@example.com"
        };
        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);
        MemberListDocument? originalDocument = viewModel.Document;
        viewModel.SetSelectedTeam(new TeamInfo("team-1", "開発", null));

        Task importing = viewModel.Import.ImportCurrentMembersCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.Import.CancelImportCurrentMembersCommand.Execute(null);
        await importing;

        Assert.Equal("old@example.com", viewModel.PastedText);
        Assert.Same(originalDocument, viewModel.Document);
        Assert.False(viewModel.Import.IsImportingMembers);
    }

    [Fact]
    public async Task MemberFile_現在メンバー取得失敗時は既存入力を維持する()
    {
        FakeTeamsGateway gateway = new()
        {
            OnGetMembers = (_, _) => Task.FromException<IReadOnlyList<TeamMember>>(
                new InvalidOperationException("Graph failed"))
        };
        FakeDialogs dialogs = new();
        RecordingNotificationService notifications = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), dialogs, notifications, gateway, dialogs)
        {
            SelectedInputIndex = 1, PastedText = "old@example.com"
        };
        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);
        MemberListDocument? originalDocument = viewModel.Document;
        viewModel.SetSelectedTeam(new TeamInfo("team-1", "開発", null));

        await viewModel.Import.ImportCurrentMembersCommand.ExecuteAsync(null);

        Assert.Equal("old@example.com", viewModel.PastedText);
        Assert.Same(originalDocument, viewModel.Document);
        Assert.Equal("現在のメンバーを取り込めませんでした", notifications.ErrorTitle);
    }

    [Fact]
    public void MemberFile_チーム未選択または入力無効時は現在メンバーを取り込めない()
    {
        FakeTeamsGateway gateway = new();
        FakeDialogs dialogs = new();
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, gateway, dialogs);

        Assert.False(viewModel.Import.ImportCurrentMembersCommand.CanExecute(null));
        viewModel.SetSelectedTeam(new TeamInfo("team-1", "開発", null));
        Assert.False(viewModel.Import.ImportCurrentMembersCommand.CanExecute(null));
        viewModel.SelectedInputIndex = 1;
        Assert.True(viewModel.Import.ImportCurrentMembersCommand.CanExecute(null));
        viewModel.SetEnabled(false);
        Assert.False(viewModel.Import.ImportCurrentMembersCommand.CanExecute(null));
    }

    [Fact]
    public async Task MemberFile_取り込み中にチームが変わると取得をキャンセルして既存入力を維持する()
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
        MemberFileViewModel viewModel = new(new FakeMemberListReader(null!), new MemberTextParser(),
            new FakePreferences(), dialogs, dialogs, gateway, dialogs)
        {
            SelectedInputIndex = 1, PastedText = "old@example.com"
        };
        await viewModel.ApplyPastedTextInputCommand.ExecuteAsync(null);
        MemberListDocument? originalDocument = viewModel.Document;
        viewModel.SetSelectedTeam(new TeamInfo("team-a", "チームA", null));

        Task importing = viewModel.Import.ImportCurrentMembersCommand.ExecuteAsync(null);
        await started.Task;
        viewModel.SetSelectedTeam(new TeamInfo("team-b", "チームB", null));
        await importing;

        Assert.Equal("old@example.com", viewModel.PastedText);
        Assert.Same(originalDocument, viewModel.Document);
        Assert.False(viewModel.Import.IsImportingMembers);
    }
}