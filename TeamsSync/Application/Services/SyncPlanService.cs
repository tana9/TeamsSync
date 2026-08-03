using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>入力一覧と現在のチーム構成から同期プランを作成し、再検証する</summary>
public sealed class SyncPlanService(ITeamsGateway teamsGateway, ILogger<SyncPlanService>? logger = null)
    : ISyncPlanService
{
    private const int AddressResolutionConcurrency = 15;
    private const int ProgressReportInterval = 25;
    private readonly ILogger<SyncPlanService> _logger = logger ?? NullLogger<SyncPlanService>.Instance;

    /// <summary>入力一覧を現メンバーおよびディレクトリと照合して同期プランを作成する</summary>
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

    /// <summary>プレビュー済みプランを最新のチーム構成で再検証する</summary>
    public async Task<SyncPlanRevalidation> RevalidatePlanAsync(SyncPlan preview,
        CancellationToken cancellationToken = default)
    {
        SyncPlan latest = await BuildPlanAsync(
            preview.Team, preview.InputAddresses, preview.Mode, cancellationToken: cancellationToken);
        return new SyncPlanRevalidation(preview.IsEquivalentTo(latest), latest);
    }

    /// <summary>実行済みプランと同じ条件で、チームの最新状態から新しいプランを作成する</summary>
    public Task<SyncPlan> ReconcileAsync(SyncPlan executedPlan,
        CancellationToken cancellationToken = default)
    {
        return BuildPlanAsync(executedPlan.Team, executedPlan.InputAddresses,
            executedPlan.Mode, cancellationToken: cancellationToken);
    }

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

    private sealed class BatchedProgressReporter(IProgress<int>? progress, int total, int interval)
    {
        private readonly Lock _gate = new();
        private readonly int _interval = interval;
        private int _completed;
        private int _nextReport = interval;

        public void Advance(int count)
        {
            lock (_gate)
            {
                _completed += count;
                if (_completed < total && _completed < _nextReport)
                {
                    return;
                }

                while (_nextReport <= _completed)
                {
                    _nextReport += _interval;
                }

                progress?.Report(_completed);
            }
        }
    }

    private sealed record AddressResolutionAttempt(AddressResolution Resolution, bool DirectorySearched);
    private sealed record AddressResolutionGroup(string Address, AddressResolutionAttempt Attempt);
    private sealed record AddressResolutionBatch(
        AddressResolution[] Resolutions, int UniqueInputCount, int DirectorySearchCount);
}