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
}