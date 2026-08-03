using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Tests.Unit.Application;

public sealed class SyncExecutionCoordinatorTests
{
    private static readonly TeamInfo Team = new("team-1", "開発", null);

    [Fact]
    public async Task Execute_同期結果を保存して最新状態を返す()
    {
        FakeTeamsGateway gateway = new();
        FakeResultWriter writer = new() { ResultPath = @"C:\Logs\result.csv" };
        SyncExecutionCoordinator coordinator = new(new SyncPlanService(gateway), new SyncExecutor(gateway), writer);
        SyncPlan plan = new(Team, [], [], Mode: SyncMode.AddOnly);
        SyncAuditContext audit = new(Guid.NewGuid(), "members.csv", "hash");
        int reconciliationStarts = 0;

        SyncExecutionOutcome outcome = await coordinator.ExecuteAsync(plan, audit,
            reconciliationStarting: () => reconciliationStarts++,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(outcome.Execution.Cancelled);
        Assert.Equal(writer.ResultPath, outcome.ResultLogPath);
        Assert.Null(outcome.LogSaveError);
        Assert.NotNull(outcome.RemainingPlan);
        Assert.Null(outcome.ReconciliationError);
        Assert.Equal(1, reconciliationStarts);
    }

    [Fact]
    public async Task Execute_キャンセル後も既にキャンセル済みのトークンを使わず最新状態を再取得する()
    {
        FakeTeamsGateway gateway = new()
        {
            OnAdd = (_, _, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
            // 渡されたトークンが既にキャンセル済みの場合に失敗させることで、
            // ExecuteAsyncがキャンセル済みトークンをそのまま再取得へ流用していないことを検証する
            OnGetMembers = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<TeamMember>>([]);
            }
        };
        FakeResultWriter writer = new() { ResultPath = @"C:\Logs\result.csv" };
        SyncExecutionCoordinator coordinator = new(new SyncPlanService(gateway), new SyncExecutor(gateway), writer);
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Add, "New", "new@example.com", ChangeReason.Unspecified, "new-id")],
            ["new@example.com"], Mode: SyncMode.AddOnly);
        using CancellationTokenSource cancellation = new();

        Task<SyncExecutionOutcome> executing = coordinator.ExecuteAsync(plan,
            new SyncAuditContext(Guid.NewGuid(), "members.csv", "hash"), cancellationToken: cancellation.Token);
        await Task.Yield();
        cancellation.Cancel();
        SyncExecutionOutcome outcome = await executing;

        Assert.True(outcome.Execution.Cancelled);
        Assert.Null(outcome.ReconciliationError);
        Assert.NotNull(outcome.RemainingPlan);
    }

    [Fact]
    public async Task Execute_ログ保存と再取得の失敗を同期結果と分けて返す()
    {
        FakeTeamsGateway gateway = new()
        {
            OnGetMembers = (_, _) => Task.FromException<IReadOnlyList<TeamMember>>(
                new InvalidOperationException("refresh failed"))
        };
        FakeResultWriter writer = new() { Exception = new IOException("save failed") };
        SyncExecutionCoordinator coordinator = new(new SyncPlanService(gateway), new SyncExecutor(gateway), writer);
        SyncPlan plan = new(Team, [], [], Mode: SyncMode.AddOnly);

        SyncExecutionOutcome outcome = await coordinator.ExecuteAsync(plan,
            new SyncAuditContext(Guid.NewGuid(), "members.csv", "hash"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(outcome.Execution.Cancelled);
        Assert.IsType<IOException>(outcome.LogSaveError);
        Assert.Empty(outcome.ResultLogPath);
        Assert.Null(outcome.RemainingPlan);
        Assert.IsType<InvalidOperationException>(outcome.ReconciliationError);
    }

    [Fact]
    public async Task RevalidateAndExecuteAsync_プレビュー後にチーム構成が変化していれば実行せず最新プランを返す()
    {
        // 実行経路(RevalidateAndExecuteAsync)を直接呼び出すだけで、呼び出し元(ViewModel等)が
        // 別途再検証を挟まなくても、プレビュー後の状態変化(所有者への昇格を含む)を
        // 取りこぼさずに実行を阻止できることを検証する
        FakeTeamsGateway gateway = new();
        SyncPlanService plans = new(gateway);
        SyncExecutionCoordinator coordinator = new(plans, new SyncExecutor(gateway), new FakeResultWriter());
        SyncPlan preview = await plans.BuildPlanAsync(Team, [], SyncMode.FullSync,
            cancellationToken: TestContext.Current.CancellationToken);

        // プレビュー後、他の操作でチームのメンバー構成が変化したと仮定する
        gateway.Members = [new TeamMember("m1", "u1", "New", "new@example.com", false)];

        SyncExecutionAttempt attempt = await coordinator.RevalidateAndExecuteAsync(preview,
            new SyncAuditContext(Guid.NewGuid(), "members.csv", "hash"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(attempt.IsStale);
        Assert.Null(attempt.Outcome);
        Assert.Empty(gateway.Added);
        Assert.Empty(gateway.Removed);
    }
}