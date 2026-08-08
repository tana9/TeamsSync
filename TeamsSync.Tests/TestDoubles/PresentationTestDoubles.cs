using System.Runtime.ExceptionServices;
using System.Windows.Threading;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Files;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels;

using Xunit.Sdk;

namespace TeamsSync.Tests.TestDoubles;

internal static class SyncWorkspaceViewModelFactory
{
    public static SyncWorkspaceViewModel Create(ISyncPlanService planService, ISyncExecutor executor,
        ISyncResultWriter resultWriter, ISyncConfirmationService confirmation, INotificationService notifications,
        ISavedFileLauncher? savedFileLauncher = null)
    {
        SyncExecutionCoordinator coordinator = new(planService, executor, resultWriter);
        return new SyncWorkspaceViewModel(coordinator, confirmation, notifications,
            savedFileLauncher ?? new UnavailableSavedFileLauncher());
    }

    private sealed class UnavailableSavedFileLauncher : ISavedFileLauncher
    {
        public void Open(string path)
        {
            throw new InvalidOperationException("保存済みファイルを開くサービスが構成されていません。");
        }
    }
}

// Presentation層のテストから共有する、外部依存を持たないテストダブルとDispatcherヘルパー。
// 各テストは必要な振る舞いだけをプロパティやデリゲートで明示的に設定する
internal static class DispatcherTestHelper
{
    // DispatcherSynchronizationContext配下でTask.Runを含む非同期処理を、その完了までテストスレッドをブロックして
    // 同期的に待つためのヘルパー。Dispatcher.PushFrameでメッセージポンプを回すことで、Task.Run完了後の継続が
    // (DispatcherSynchronizationContext.Postにより)テストスレッド上で実行されるようにする
    public static void RunOnDispatcher(Func<Task> action, TimeSpan? timeout = null)
    {
        DispatcherFrame frame = new();
        Exception? error = null;
        DispatcherTimer timer = new(DispatcherPriority.Send) { Interval = timeout ?? TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            error = new TimeoutException("Dispatcher上のテスト処理が制限時間内に完了しませんでした。");
            frame.Continue = false;
        };
        timer.Start();
        _ = RunAsync();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return;

        async Task RunAsync()
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                frame.Continue = false;
            }
        }
    }
}

internal sealed class FakeAuthenticationService : IAuthenticationService
{
    // サインアウト失敗/キャンセルのテスト用フック。未設定時は成功する
    public Exception? SignOutException { get; set; }
    public string? UserName => null;
    public string? TenantId => null;

    public Task<string> GetTokenAsync(bool interactive = false, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("token");
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        return SignOutException is null ? Task.CompletedTask : Task.FromException(SignOutException);
    }
}

internal sealed class FakeManualService : IManualService
{
    public void OpenManual()
    {
    }
}

internal sealed class ThrowingManualService : IManualService
{
    public void OpenManual()
    {
        throw new InvalidOperationException("展開に失敗しました");
    }
}

internal sealed class RecordingNotificationService : INotificationService
{
    public Action? Action { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? ErrorTitle { get; private set; }
    public string? SuccessMessage { get; private set; }
    public string? SuccessTitle { get; private set; }
    public string? WarningMessage { get; private set; }
    public string? WarningTitle { get; private set; }

    public void ShowSuccess(string title, string message)
    {
        SuccessTitle = title;
        SuccessMessage = message;
    }

    public void ShowWarning(string title, string message)
    {
        WarningTitle = title;
        WarningMessage = message;
    }

    public void ShowSuccessWithAction(string title, string message, string actionText, Action action)
    {
        ShowSuccess(title, message);
        Action = action;
    }

    public void ShowWarningWithAction(string title, string message, string actionText, Action action)
    {
        ShowWarning(title, message);
        Action = action;
    }

    public Task ShowErrorAsync(string message, string title = "エラー", Action? onClosed = null)
    {
        ErrorMessage = message;
        ErrorTitle = title;
        onClosed?.Invoke();
        return Task.CompletedTask;
    }
}

internal sealed class FakePreferences : IUserPreferences
{
    public Exception? SaveException { get; set; }
    public string? LastFolder { get; set; }

    public void Save()
    {
        if (SaveException is not null)
        {
            throw SaveException;
        }
    }
}

internal sealed class FakeMemberListReader(MemberListDocument document) : IMemberListReader
{
    public MemberListDocument Read(string path, CancellationToken cancellationToken)
    {
        return document;
    }
}

internal sealed class SwitchingMemberListReader(MemberListDocument document) : IMemberListReader
{
    public Exception? Exception { get; set; }

    public MemberListDocument Read(string path, CancellationToken cancellationToken)
    {
        if (Exception is not null)
        {
            throw Exception;
        }

        return document;
    }
}

internal sealed class BlockingMemberListReader(MemberListDocument document) : IMemberListReader
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ManualResetEventSlim Release { get; } = new(false);

    public MemberListDocument Read(string path, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        // WaitHandle.WaitAny相当でキャンセルにも反応させ、キャンセルテストがハングしないようにする
        WaitHandle.WaitAny([Release.WaitHandle, cancellationToken.WaitHandle]);
        cancellationToken.ThrowIfCancellationRequested();
        return document;
    }
}

internal sealed class BlockingTextParser : IMemberTextParser
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ManualResetEventSlim Release { get; } = new(false);

    public MemberListDocument Parse(string text, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        WaitHandle.WaitAny([Release.WaitHandle, cancellationToken.WaitHandle]);
        cancellationToken.ThrowIfCancellationRequested();
        return new MemberTextParser().Parse(text, cancellationToken);
    }
}

internal sealed class FakeResultWriter : ISyncResultWriter
{
    public Exception? Exception { get; set; }
    public string ResultPath { get; set; } = @"C:\Logs\result.csv";

    public string WriteAutoLog(SyncPlan plan, SyncOperationsResult result, SyncAuditContext auditContext)
    {
        if (Exception is not null)
        {
            throw Exception;
        }

        return ResultPath;
    }
}

internal sealed class RecordingSavedFileLauncher : ISavedFileLauncher
{
    public string? OpenedPath { get; private set; }
    public Exception? Exception { get; set; }

    public void Open(string path)
    {
        if (Exception is not null)
        {
            throw Exception;
        }

        OpenedPath = path;
    }
}

internal sealed class FakeFilePickerService : IFilePickerService
{
    public string? InitialDirectory { get; private set; }
    public string? ResultPath { get; set; }

    public string? PickMemberFile(string? initialDirectory)
    {
        InitialDirectory = initialDirectory;
        return ResultPath;
    }
}

internal sealed class FailingNotificationService : INotificationService
{
    public string WarningTitle { get; private set; } = "";
    public void ShowSuccess(string title, string message) { }

    public void ShowWarning(string title, string message)
    {
        WarningTitle = title;
    }

    public Task ShowErrorAsync(string message, string title = "エラー", Action? onClosed = null)
    {
        return Task.FromException(new XunitException(message));
    }
}

internal sealed class FakeSyncConfirmationService : ISyncConfirmationService
{
    public Func<SyncConfirmation, bool>? OnConfirm { get; init; }

    public Task<bool> ConfirmSyncAsync(SyncConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OnConfirm?.Invoke(confirmation) ?? true);
    }
}

internal sealed class FakeMemberInputConfirmationService : IMemberInputConfirmationService
{
    public Func<string, int, bool>? OnConfirmReplaceMemberInput { get; init; }
    public Func<string, int, bool>? OnConfirmReplaceTextWithFileContent { get; init; }
    public int ConfirmationCount { get; private set; }
    public int FileContentConfirmationCount { get; private set; }

    public Task<bool> ConfirmReplaceMemberInputAsync(string teamName, int memberCount,
        CancellationToken cancellationToken = default)
    {
        ConfirmationCount++;
        return Task.FromResult(OnConfirmReplaceMemberInput?.Invoke(teamName, memberCount) ?? true);
    }

    public Task<bool> ConfirmReplaceTextWithFileContentAsync(string fileName, int memberCount,
        CancellationToken cancellationToken = default)
    {
        FileContentConfirmationCount++;
        return Task.FromResult(OnConfirmReplaceTextWithFileContent?.Invoke(fileName, memberCount) ?? true);
    }
}

// 複数の対話サービスを同じ状態で検証する既存の統合的ViewModelテスト向けアダプター。
// 個々の振る舞いは責務別テストダブルへ委譲し、新規テストでは各サービスを直接使用する
internal sealed class FakeDialogs : IFilePickerService, ISyncConfirmationService,
    IMemberInputConfirmationService, INotificationService
{
    private readonly FakeFilePickerService _filePicker;
    private readonly FakeMemberInputConfirmationService _inputConfirmation;
    private readonly FailingNotificationService _notifications = new();
    private readonly FakeSyncConfirmationService _syncConfirmation;

    public FakeDialogs()
    {
        _filePicker = new FakeFilePickerService();
        _syncConfirmation = new FakeSyncConfirmationService { OnConfirm = value => OnConfirm?.Invoke(value) ?? true };
        _inputConfirmation = new FakeMemberInputConfirmationService
        {
            OnConfirmReplaceMemberInput = (team, count) => OnConfirmReplaceMemberInput?.Invoke(team, count) ?? true,
            OnConfirmReplaceTextWithFileContent = (file, count) =>
                OnConfirmReplaceTextWithFileContent?.Invoke(file, count) ?? true
        };
    }

    public Func<SyncConfirmation, bool>? OnConfirm { get; init; }
    public Func<string, int, bool>? OnConfirmReplaceMemberInput { get; init; }
    public Func<string, int, bool>? OnConfirmReplaceTextWithFileContent { get; init; }
    public int ReplaceMemberInputConfirmationCount => _inputConfirmation.ConfirmationCount;
    public int FileContentConfirmationCount => _inputConfirmation.FileContentConfirmationCount;
    public string WarningTitle => _notifications.WarningTitle;

    public string? PickMemberFile(string? initialDirectory)
    {
        return _filePicker.PickMemberFile(initialDirectory);
    }

    public Task<bool> ConfirmReplaceMemberInputAsync(string teamName, int memberCount,
        CancellationToken cancellationToken = default)
    {
        return _inputConfirmation.ConfirmReplaceMemberInputAsync(teamName, memberCount, cancellationToken);
    }

    public Task<bool> ConfirmReplaceTextWithFileContentAsync(string fileName, int memberCount,
        CancellationToken cancellationToken = default)
    {
        return _inputConfirmation.ConfirmReplaceTextWithFileContentAsync(fileName, memberCount, cancellationToken);
    }

    public void ShowSuccess(string title, string message)
    {
        _notifications.ShowSuccess(title, message);
    }

    public void ShowWarning(string title, string message)
    {
        _notifications.ShowWarning(title, message);
    }

    public Task ShowErrorAsync(string message, string title = "エラー", Action? onClosed = null)
    {
        return _notifications.ShowErrorAsync(message, title, onClosed);
    }

    public Task<bool> ConfirmSyncAsync(SyncConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        return _syncConfirmation.ConfirmSyncAsync(confirmation, cancellationToken);
    }
}

internal sealed class FakeTeamsGateway : ITeamsGateway
{
    public IReadOnlyList<TeamInfo> OwnedTeams { get; set; } = [];
    public IReadOnlyList<TeamMember> Members { get; set; } = [];
    public Dictionary<string, DirectoryUser> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string TeamId, string UserId)> Added { get; } = [];
    public List<(string TeamId, string MembershipId)> Removed { get; } = [];
    public Func<string, string, CancellationToken, Task>? OnAdd { get; set; }
    public Func<string, string, CancellationToken, Task>? OnRemove { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<TeamMember>>>? OnGetMembers { get; set; }

    public Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(("current-user", "Current User", "current@example.com"));
    }

    public Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OwnedTeams);
    }

    public Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId,
        CancellationToken cancellationToken = default)
    {
        return OnGetMembers?.Invoke(teamId, cancellationToken) ?? Task.FromResult(Members);
    }

    public Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DirectoryUser>>(
            Users.TryGetValue(identifier, out DirectoryUser? user) ? [user] : []);
    }

    public Task AddMemberAsync(string teamId, string userId,
        CancellationToken cancellationToken = default)
    {
        Added.Add((teamId, userId));
        return OnAdd?.Invoke(teamId, userId, cancellationToken) ?? Task.CompletedTask;
    }

    public Task RemoveMemberAsync(string teamId, string membershipId,
        CancellationToken cancellationToken = default)
    {
        Removed.Add((teamId, membershipId));
        return OnRemove?.Invoke(teamId, membershipId, cancellationToken) ?? Task.CompletedTask;
    }

    public static implicit operator TeamsAccessService(FakeTeamsGateway gateway)
    {
        return new TeamsAccessService(gateway);
    }
}