using System.Windows.Threading;
using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Infrastructure.Files;
using TeamsSync.Presentation.Services;
using Xunit.Sdk;

namespace TeamsSync.Tests;

// MainWindowViewModelTests / TeamSelectionViewModelTests / MemberFileViewModelTests /
// SyncWorkspaceViewModelTestsの各テストクラスから共通して参照されるFakeクラス群とテストヘルパー。
// 元はPresentationViewModelTests内のprivateなネストクラスだったが、テストファイルを対象VMごとに
// 分割したことで複数のテストクラスから参照する必要が生じたため、このファイルへ集約している。
internal static class DispatcherTestHelper
{
    // DispatcherSynchronizationContext配下でTask.Runを含む非同期処理を、その完了までテストスレッドをブロックして
    // 同期的に待つためのヘルパー。Dispatcher.PushFrameでメッセージポンプを回すことで、Task.Run完了後の継続が
    // (DispatcherSynchronizationContext.Postにより)テストスレッド上で実行されるようにする。
    public static void RunOnDispatcher(Func<Task> action)
    {
        var frame = new DispatcherFrame();
        Exception? error = null;
        _ = RunAsync();
        Dispatcher.PushFrame(frame);
        if (error is not null)
            throw error;
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
    public string? UserName => null;
    public string? TenantId => null;

    // サインアウト失敗/キャンセルのテスト用フック。未設定時は成功する。
    public Exception? SignOutException { get; set; }

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
    public string? ErrorMessage { get; private set; }
    public string? ErrorTitle { get; private set; }
    public string? WarningMessage { get; private set; }
    public string? WarningTitle { get; private set; }

    public void ShowSuccess(string title, string message)
    {
    }

    public void ShowWarning(string title, string message)
    {
        WarningTitle = title;
        WarningMessage = message;
    }

    public void ShowError(string message, string title = "エラー", Action? onClosed = null)
    {
        ErrorMessage = message;
        ErrorTitle = title;
        onClosed?.Invoke();
    }
}

internal sealed class FakePreferences : IUserPreferences
{
    public string? LastFolder { get; set; }
    public Exception? SaveException { get; set; }

    public void Save()
    {
        if (SaveException is not null) throw SaveException;
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
        if (Exception is not null) throw Exception;
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

    public MemberListDocument Parse(string text)
    {
        Started.TrySetResult();
        Release.Wait(TestContext.Current.CancellationToken);
        return new MemberTextParser().Parse(text);
    }
}

internal sealed class FakeResultWriter : ISyncResultWriter
{
    public Exception? Exception { get; set; }

    public void WriteCsv(string path, SyncPlan plan, SyncExecutionResult result)
    {
        if (Exception is not null) throw Exception;
    }
}

internal sealed class FakeDialogs : IFilePickerService, ISyncConfirmationService, INotificationService
{
    public Func<SyncConfirmation, bool>? OnConfirm { get; init; }
    public string? ResultPath { get; init; }
    public string WarningTitle { get; private set; } = "";

    public string? PickMemberFile(string? initialDirectory)
    {
        return null;
    }

    public string? PickResultFile(string? initialDirectory, string teamName)
    {
        return ResultPath;
    }

    public void ShowSuccess(string title, string message)
    {
    }

    public void ShowWarning(string title, string message)
    {
        WarningTitle = title;
    }

    public void ShowError(string message, string title = "エラー", Action? onClosed = null)
    {
        throw new XunitException(message);
    }

    public Task<bool> ConfirmSyncAsync(SyncConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OnConfirm?.Invoke(confirmation) ?? true);
    }
}

internal sealed class FakeTeamsGateway : ITeamsGateway
{
    public IReadOnlyList<TeamInfo> OwnedTeams { get; init; } = [];
    public IReadOnlyList<TeamMember> Members { get; set; } = [];
    public Dictionary<string, DirectoryUser> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string TeamId, string UserId)> Added { get; } = [];
    public List<(string TeamId, string MembershipId)> Removed { get; } = [];
    public Func<string, string, CancellationToken, Task>? OnAdd { get; set; }
    public Func<string, string, CancellationToken, Task>? OnRemove { get; set; }

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
        return Task.FromResult(Members);
    }

    public Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DirectoryUser>>(
            Users.TryGetValue(identifier, out var user) ? [user] : []);
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
}
