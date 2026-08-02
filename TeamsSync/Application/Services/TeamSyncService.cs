using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>
///     入力アドレス一覧と現メンバーを突き合わせて同期プランを作成し、そのプランに基づいて
///     メンバーの追加・削除を実行する。アドレスの解決判定自体は<see cref="AddressResolver" />に委ね、
///     このクラスはGraph呼び出し・並列制御・進捗通知だけを担う。
/// </summary>
public sealed class TeamSyncService(ITeamsGateway teamsGateway, ILogger<TeamSyncService>? logger = null)
{
    private const int AddressResolutionConcurrency = 15;
    private const int ProgressReportInterval = 25;

    // Graphのレート制限を悪化させないよう、追加・削除操作の間に挟む待機時間。
    // Microsoft Graph公式ドキュメントのソース(throttling-teams.md)によると、
    // POST /teams/{team-id}/members の「リソース(チーム)あたり」の上限は表記が「4rpm」
    // (4リクエスト/分)だが、同じページの補足文では「A maximum of four requests per
    // second per app can be issued on a given team.」(4リクエスト/秒)となっており、
    // ドキュメント内で表記が食い違っている。
    // https://github.com/microsoftgraph/microsoft-graph-docs-contrib/blob/main/includes/throttling-teams.md
    // 300msは「秒4リクエスト」の解釈(緩い方)に約20%の余裕を残した値。「分4リクエスト」が
    // 正しい場合はこの値では429が頻発する可能性があるため、実テナントでの429発生有無の
    // 確認が必要(TODO.md「同期実行の操作間ディレイを見直す」参照)。
    // 実際にスロットリング(429)された場合はAddStandardResilienceHandler(DependencyInjection.cs)が
    // Retry-Afterに従って自動再試行する。POST/DELETEは非冪等操作のため429以外(503・タイムアウト等)は
    // 重複実行を避けるため再試行しない(DependencyInjection.AllowThrottlingRetryForUnsafeHttpMethods参照)。
    private static readonly TimeSpan OperationThrottleDelay = TimeSpan.FromMilliseconds(300);

    private readonly ILogger<TeamSyncService> _logger = logger ?? NullLogger<TeamSyncService>.Instance;

    /// <summary>
    ///     入力アドレス一覧を現メンバーおよびディレクトリと突き合わせて解決し、
    ///     追加・削除・維持・エラーの各変更内容をまとめた同期プランを作成する。
    /// </summary>
    public async Task<SyncPlan> BuildPlanAsync(TeamInfo team, IReadOnlyList<string> addresses,
        SyncMode mode = SyncMode.FullSync, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<TeamMember> current = await teamsGateway.GetTeamMembersAsync(team.Id, cancellationToken);
        TeamRoster roster = new(current);
        AddressResolutionBatch resolutionBatch = await ResolveAddressesAsync(addresses, roster, progress,
            cancellationToken);

        SyncPlan plan = SyncPlanFactory.Create(team, roster, resolutionBatch.Resolutions, mode, addresses);
        _logger.LogInformation(
            "同期差分を作成しました。TeamId={TeamId}, Add={Add}, Remove={Remove}, Errors={Errors}, " +
            "InputCount={InputCount}, UniqueInputCount={UniqueInputCount}, DirectorySearchCount={DirectorySearchCount}, " +
            "ElapsedMilliseconds={ElapsedMilliseconds}",
            team.Id, plan.AddCount, plan.RemoveCount, plan.ErrorCount, addresses.Count,
            resolutionBatch.UniqueInputCount, resolutionBatch.DirectorySearchCount, stopwatch.ElapsedMilliseconds);
        return plan;
    }

    /// <summary>
    ///     入力アドレス一覧を、同時実行数を制限しつつ入力順を維持して解決する。
    /// </summary>
    private async Task<AddressResolutionBatch> ResolveAddressesAsync(IReadOnlyList<string> addresses,
        TeamRoster current, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using SemaphoreSlim concurrency = new(AddressResolutionConcurrency);
        BatchedProgressReporter progressReporter = new(progress, addresses.Count, ProgressReportInterval);
        List<IGrouping<string, string>> groups = addresses
            .GroupBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AddressResolutionGroup[] resolvedGroups = await Task.WhenAll(groups.Select(async group =>
        {
            AddressResolutionAttempt attempt = await ResolveAddressAsync(
                group.Key, current, concurrency, cancellationToken);
            progressReporter.Advance(group.Count());
            return new AddressResolutionGroup(group.Key, attempt);
        }));

        Dictionary<string, AddressResolution> byAddress = resolvedGroups.ToDictionary(
            group => group.Address, group => group.Attempt.Resolution, StringComparer.OrdinalIgnoreCase);
        AddressResolution[] resolutions = addresses
            .Select(address => byAddress[address] with { Address = address })
            .ToArray();
        return new AddressResolutionBatch(resolutions, groups.Count,
            resolvedGroups.Count(group => group.Attempt.DirectorySearched));
    }

    /// <summary>
    ///     1件のアドレスを解決する。現メンバーの中に一致があればそれを優先し、
    ///     なければ同時実行数を制限しつつディレクトリ検索(<see cref="ITeamsGateway.FindUsersAsync" />)を行う。
    /// </summary>
    private async Task<AddressResolutionAttempt> ResolveAddressAsync(string address, TeamRoster current,
        SemaphoreSlim concurrency, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddressResolution? local = AddressResolver.TryResolveFromRoster(address, current);
        if (local is not null)
        {
            return new AddressResolutionAttempt(local, false);
        }

        await concurrency.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<DirectoryUser> candidates = await teamsGateway.FindUsersAsync(address, cancellationToken);
            return new AddressResolutionAttempt(
                AddressResolver.ResolveFromDirectory(address, candidates, current), true);
        }
        finally
        {
            concurrency.Release();
        }
    }

    /// <summary>並列処理の完了件数を一定間隔と最終件だけ通知する。</summary>
    private sealed class BatchedProgressReporter
    {
        private readonly object _gate = new();
        private readonly int _interval;
        private readonly IProgress<int>? _progress;
        private readonly int _total;
        private int _completed;
        private int _nextReport;

        public BatchedProgressReporter(IProgress<int>? progress, int total, int interval)
        {
            _progress = progress;
            _total = total;
            _interval = interval;
            _nextReport = interval;
        }

        public void Advance(int count)
        {
            lock (_gate)
            {
                _completed += count;
                if (_completed < _total && _completed < _nextReport)
                {
                    return;
                }

                while (_nextReport <= _completed)
                {
                    _nextReport += _interval;
                }

                _progress?.Report(_completed);
            }
        }
    }

    /// <summary>
    ///     プレビュー済みのプランが最新の状態と一致しているかどうかを再検証する。
    ///     実行までの間にメンバーシップが変化していた場合は最新のプランを返す。
    /// </summary>
    public async Task<SyncPlanRevalidation> RevalidatePlanAsync(SyncPlan preview,
        CancellationToken cancellationToken = default)
    {
        SyncPlan latest = await BuildPlanAsync(
            preview.Team, preview.InputAddresses, preview.Mode, cancellationToken: cancellationToken);
        return new SyncPlanRevalidation(preview.IsEquivalentTo(latest), latest);
    }

    /// <summary>
    ///     実行済みプランと同じチーム・入力アドレスに対して、実行後の実際の状態を反映した
    ///     新しいプランを作成する(実行結果の確認用)。
    /// </summary>
    public Task<SyncPlan> ReconcileAsync(SyncPlan executedPlan,
        CancellationToken cancellationToken = default)
    {
        return BuildPlanAsync(executedPlan.Team, executedPlan.InputAddresses,
            executedPlan.Mode, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     同期プランに含まれる追加・削除操作を順に実行する。未解決の変更がある場合、
    ///     または追加のみモードで削除が含まれる場合は例外をスローする。
    /// </summary>
    public async Task<SyncExecutionResult> ExecuteAsync(SyncPlan plan,
        IProgress<SyncProgress>? progress = null, SyncAuditContext? auditContext = null,
        CancellationToken cancellationToken = default)
    {
        plan.EnsureExecutable();
        using IDisposable? auditScope = BeginAuditScope(plan, auditContext);
        return await ExecuteOperationsAsync(plan, progress, cancellationToken);
    }

    private IDisposable? BeginAuditScope(SyncPlan plan, SyncAuditContext? auditContext)
    {
        SyncAuditContext context = auditContext ?? new SyncAuditContext(Guid.NewGuid(), "", "");
        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["ExecutionId"] = context.ExecutionId,
            ["TeamId"] = plan.Team.Id,
            ["InputFileName"] = context.InputFileName,
            ["InputFileSha256"] = context.InputFileSha256,
            ["TenantId"] = context.TenantId,
            ["ActorObjectId"] = context.ActorObjectId,
            ["SyncMode"] = plan.Mode
        });
    }

    private async Task<SyncExecutionResult> ExecuteOperationsAsync(SyncPlan plan,
        IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
    {
        List<SyncOperationResult> results = [];
        IReadOnlyList<SyncChange> operations = plan.Operations;
        _logger.LogInformation(
            "SyncStarted Add={AddCount} Remove={RemoveCount} Keep={KeepCount} Protected={ProtectedCount}",
            plan.AddCount, plan.RemoveCount, plan.KeepCount, plan.ProtectedCount);

        SyncExecutionResult BuildCancelledResult()
        {
            LogSyncCancelled(results, operations.Count);
            return new SyncExecutionResult(results, true);
        }

        for (int i = 0; i < operations.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return BuildCancelledResult();
            }

            SyncChange change = operations[i];
            progress?.Report(new SyncProgress(i, operations.Count, change.Kind, change.Email));
            bool cancelled = await ExecuteChangeAsync(plan.Team.Id, change, i, operations.Count, progress, results,
                cancellationToken);
            if (cancelled)
            {
                return BuildCancelledResult();
            }
        }

        SyncExecutionResult result = new(results, false);
        _logger.LogInformation("SyncCompleted Success={SuccessCount} Failure={FailureCount}",
            result.SuccessCount, result.FailureCount);
        return result;
    }

    /// <summary>
    ///     1件の追加・削除操作を実行し、結果を<paramref name="results" />へ追加する。
    ///     成功結果はスロットリング用のdelay前にresultsへ積むため、delay中にキャンセルされても
    ///     直前の操作の成功結果は失われない。戻り値はキャンセルされたかどうかのみを表す。
    /// </summary>
    private async Task<bool> ExecuteChangeAsync(string teamId, SyncChange change, int index, int totalOperations,
        IProgress<SyncProgress>? progress, List<SyncOperationResult> results, CancellationToken cancellationToken)
    {
        string? targetObjectId = change.Kind == ChangeKind.Add ? change.UserId : change.MembershipId;
        _logger.LogInformation("MemberOperationStarted Kind={Kind} TargetObjectId={TargetObjectId}",
            change.Kind, targetObjectId);
        try
        {
            if (change.Kind == ChangeKind.Add)
            {
                await teamsGateway.AddMemberAsync(teamId, change.UserId!, cancellationToken);
            }
            else
            {
                await teamsGateway.RemoveMemberAsync(teamId, change.MembershipId!, cancellationToken);
            }

            results.Add(new SyncOperationResult(change.Kind, change.Email, true, null, change.DisplayName));
            _logger.LogInformation("MemberOperationSucceeded Kind={Kind} TargetObjectId={TargetObjectId}",
                change.Kind, targetObjectId);
            progress?.Report(new SyncProgress(index + 1, totalOperations, change.Kind, change.Email));
            if (index < totalOperations - 1)
            {
                await Task.Delay(OperationThrottleDelay, cancellationToken);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception ex)
        {
            results.Add(new SyncOperationResult(change.Kind, change.Email, false, ex.Message, change.DisplayName));
            _logger.LogWarning(
                "MemberOperationFailed Kind={Kind} TargetObjectId={TargetObjectId} ErrorType={ErrorType}",
                change.Kind, targetObjectId, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    ///     キャンセルによる同期中断を、成功・失敗件数とともに警告ログへ出力する。
    /// </summary>
    private void LogSyncCancelled(IReadOnlyList<SyncOperationResult> results, int totalOperations)
    {
        _logger.LogWarning("SyncCancelled Success={SuccessCount} Failure={FailureCount} Unexecuted={UnexecutedCount}",
            results.Count(result => result.Succeeded), results.Count(result => !result.Succeeded),
            Math.Max(0, totalOperations - results.Count));
    }

    private sealed record AddressResolutionAttempt(AddressResolution Resolution, bool DirectorySearched);

    private sealed record AddressResolutionGroup(string Address, AddressResolutionAttempt Attempt);

    private sealed record AddressResolutionBatch(
        AddressResolution[] Resolutions, int UniqueInputCount, int DirectorySearchCount);
}
