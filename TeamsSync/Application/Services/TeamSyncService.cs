using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

public sealed class TeamSyncService(ITeamsGateway teamsGateway, ILogger<TeamSyncService>? logger = null)
{
    private const int AddressResolutionConcurrency = 15;
    private readonly ILogger<TeamSyncService> _logger = logger ?? NullLogger<TeamSyncService>.Instance;

    public async Task<SyncPlan> BuildPlanAsync(TeamInfo team, IReadOnlyList<string> addresses,
        CancellationToken cancellationToken = default, SyncMode mode = SyncMode.FullSync,
        IProgress<int>? progress = null)
    {
        var current = await teamsGateway.GetTeamMembersAsync(team.Id, cancellationToken);
        var resolutions = await ResolveAddressesAsync(addresses, current, progress, cancellationToken);

        var (resolved, changes) = ClassifyResolutions(resolutions);
        if (mode == SyncMode.FullSync)
            changes.AddRange(ComputeRemovals(current, resolved, changes));

        var snapshot = BuildMembershipSnapshot(current);
        var plan = new SyncPlan(team, changes, addresses, current.Count(x => !x.IsOwner), snapshot, mode);
        _logger.LogInformation("同期差分を作成しました。TeamId={TeamId}, Add={Add}, Remove={Remove}, Errors={Errors}",
            team.Id, plan.AddCount, plan.RemoveCount, plan.ErrorCount);
        return plan;
    }

    private async Task<AddressResolution[]> ResolveAddressesAsync(IReadOnlyList<string> addresses,
        IReadOnlyList<TeamMember> current, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var concurrency = new SemaphoreSlim(AddressResolutionConcurrency);
        var resolvedCount = 0;
        return await Task.WhenAll(addresses.Select(async address =>
        {
            var resolution = await ResolveAddressAsync(address, current, concurrency, cancellationToken);
            progress?.Report(Interlocked.Increment(ref resolvedCount));
            return resolution;
        }));
    }

    private static (Dictionary<string, DirectoryUser> Resolved, List<SyncChange> Changes) ClassifyResolutions(
        IReadOnlyList<AddressResolution> resolutions)
    {
        var resolved = new Dictionary<string, DirectoryUser>(StringComparer.OrdinalIgnoreCase);
        var desiredUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<SyncChange>();
        foreach (var resolution in resolutions)
        {
            switch (resolution)
            {
                case { Outcome: ResolutionOutcome.Error }:
                    changes.Add(new SyncChange(ChangeKind.Error, "", resolution.Address,
                        resolution.ErrorMessage!));
                    break;
                case { Outcome: ResolutionOutcome.ExistingSingle, ExistingMember: { } existing }:
                    var existingIsDuplicate = !desiredUserIds.Add(existing.UserId);
                    changes.Add(new SyncChange(existing.IsOwner ? ChangeKind.Protected : ChangeKind.Keep,
                        existing.DisplayName, resolution.Address,
                        existingIsDuplicate
                            ? "同一ユーザーの重複指定です"
                            : existing.IsOwner ? "所有者のため変更しません" : "既にメンバーです",
                        existing.UserId, existing.MembershipId));
                    break;
                case { Outcome: ResolutionOutcome.SameUserDifferentAddress, ExistingMember: { } sameUser,
                    User: { } sameUserAccount }:
                    var sameUserIsDuplicate = !desiredUserIds.Add(sameUser.UserId);
                    changes.Add(new SyncChange(sameUser.IsOwner ? ChangeKind.Protected : ChangeKind.Keep,
                        sameUser.DisplayName, resolution.Address,
                        sameUserIsDuplicate
                            ? "同一ユーザーの重複指定です"
                            : sameUser.IsOwner ? "所有者のため変更しません" : "別のアドレスで既にメンバーです",
                        sameUser.UserId, sameUser.MembershipId));
                    resolved[resolution.Address] = sameUserAccount;
                    break;
                case { Outcome: ResolutionOutcome.NewUser, User: { } user }:
                    resolved[resolution.Address] = user;
                    var isDuplicate = !desiredUserIds.Add(user.Id);
                    changes.Add(new SyncChange(isDuplicate ? ChangeKind.Keep : ChangeKind.Add,
                        user.DisplayName, resolution.Address,
                        isDuplicate ? "同一ユーザーの重複指定です" : "メンバーに追加します", user.Id));
                    break;
            }
        }

        return (resolved, changes);
    }

    private static IEnumerable<SyncChange> ComputeRemovals(IReadOnlyList<TeamMember> current,
        Dictionary<string, DirectoryUser> resolved, IReadOnlyList<SyncChange> changes)
    {
        var wantedIds = resolved.Values.Select(x => x.Id)
            .Concat(changes.Where(x => x.Kind is ChangeKind.Keep or ChangeKind.Protected)
                .Select(x => x.UserId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return current.Where(x => !x.IsOwner && !wantedIds.Contains(x.UserId))
            .Select(member => new SyncChange(ChangeKind.Remove, member.DisplayName, member.Email,
                "リストにないため削除します", member.UserId, member.MembershipId));
    }

    private static List<TeamMembershipSnapshot> BuildMembershipSnapshot(IReadOnlyList<TeamMember> current)
    {
        return current
            .Select(member => new TeamMembershipSnapshot(
                member.MembershipId, member.UserId, member.IsOwner))
            .OrderBy(member => member.MembershipId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.UserId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<AddressResolution> ResolveAddressAsync(string address, IReadOnlyList<TeamMember> current,
        SemaphoreSlim concurrency, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var local = TryResolveFromCurrentMembers(address, current);
        if (local is not null) return local;

        await concurrency.WaitAsync(cancellationToken);
        try
        {
            var candidates = await teamsGateway.FindUsersAsync(address, cancellationToken);
            return ResolveFromCandidates(address, candidates, current);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private static AddressResolution? TryResolveFromCurrentMembers(string address, IReadOnlyList<TeamMember> current)
    {
        var existingMatches = current.Where(member =>
            string.Equals(member.Email, address, StringComparison.OrdinalIgnoreCase) ||
            UserIdentifier.NameEquals(member.DisplayName, address)).ToList();
        if (existingMatches.Count > 1)
            return new AddressResolution(ResolutionOutcome.Error, address,
                ErrorMessage: "同じ氏名のチームメンバーが複数いるため特定できません");
        return existingMatches.Count == 1
            ? new AddressResolution(ResolutionOutcome.ExistingSingle, address, ExistingMember: existingMatches[0])
            : null;
    }

    private static AddressResolution ResolveFromCandidates(string address,
        IReadOnlyList<DirectoryUser> candidates, IReadOnlyList<TeamMember> current)
    {
        var exactMatches = candidates
            .DistinctBy(user => user.Id, StringComparer.OrdinalIgnoreCase).ToList();
        if (exactMatches.Count != 1)
            return new AddressResolution(ResolutionOutcome.Error, address,
                ErrorMessage: exactMatches.Count > 1
                    ? "同じ氏名のユーザーが複数いるため、メールアドレスで指定してください"
                    : "ユーザーが見つかりません");

        var user = exactMatches[0];
        var sameUser = current.FirstOrDefault(x =>
            string.Equals(x.UserId, user.Id, StringComparison.OrdinalIgnoreCase));
        return sameUser is not null
            ? new AddressResolution(ResolutionOutcome.SameUserDifferentAddress, address,
                ExistingMember: sameUser, User: user)
            : new AddressResolution(ResolutionOutcome.NewUser, address, User: user);
    }

    private enum ResolutionOutcome
    {
        Error,
        ExistingSingle,
        NewUser,
        SameUserDifferentAddress
    }

    private sealed record AddressResolution(
        ResolutionOutcome Outcome,
        string Address,
        TeamMember? ExistingMember = null,
        DirectoryUser? User = null,
        string? ErrorMessage = null);

    public async Task<SyncPlanRevalidation> RevalidatePlanAsync(SyncPlan preview,
        CancellationToken cancellationToken = default, SyncMode mode = SyncMode.FullSync)
    {
        var latest = await BuildPlanAsync(
            preview.Team, preview.InputAddresses, cancellationToken, preview.Mode);
        return new SyncPlanRevalidation(PlansAreEquivalent(preview, latest), latest);
    }

    public Task<SyncPlan> ReconcileAsync(SyncPlan executedPlan,
        CancellationToken cancellationToken = default)
    {
        return BuildPlanAsync(executedPlan.Team, executedPlan.InputAddresses,
            cancellationToken, executedPlan.Mode);
    }

    public async Task<SyncExecutionResult> ExecuteAsync(SyncPlan plan,
        IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default,
        SyncAuditContext? auditContext = null)
    {
        if (plan.HasErrors)
            throw new InvalidOperationException("未解決ユーザーがあります。同期は実行できません。");
        if (plan.Mode == SyncMode.AddOnly && plan.RemoveCount > 0)
            throw new InvalidOperationException("追加のみモードではメンバーを削除できません。");
        var context = auditContext ?? new SyncAuditContext(Guid.NewGuid(), "", "");
        using var auditScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["ExecutionId"] = context.ExecutionId,
            ["TeamId"] = plan.Team.Id,
            ["InputFileName"] = context.InputFileName,
            ["InputFileSha256"] = context.InputFileSha256,
            ["TenantId"] = context.TenantId,
            ["ActorObjectId"] = context.ActorObjectId,
            ["SyncMode"] = plan.Mode
        });
        var results = new List<SyncOperationResult>();
        var operations = plan.Changes.Where(x => x.Kind is ChangeKind.Add or ChangeKind.Remove).ToList();
        _logger.LogInformation(
            "SyncStarted Add={AddCount} Remove={RemoveCount} Keep={KeepCount} Protected={ProtectedCount}",
            plan.AddCount, plan.RemoveCount, plan.KeepCount, plan.ProtectedCount);
        for (var i = 0; i < operations.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                LogSyncCancelled(results);
                return new SyncExecutionResult(results, true);
            }

            var change = operations[i];
            progress?.Report(new SyncProgress(i, operations.Count, change.Kind, change.Email));
            var cancelled = await ExecuteChangeAsync(plan.Team.Id, change, i, operations.Count, progress, results,
                cancellationToken);
            if (cancelled)
            {
                LogSyncCancelled(results);
                return new SyncExecutionResult(results, true);
            }
        }

        var result = new SyncExecutionResult(results, false);
        _logger.LogInformation("SyncCompleted Success={SuccessCount} Failure={FailureCount}",
            result.SuccessCount, result.FailureCount);
        return result;
    }

    // 成功結果はdelay(スロットリング)前にresultsへ積むため、delay中にキャンセルされても
    // 直前の操作の成功結果は失われない。戻り値はキャンセルされたかどうかのみを表す。
    private async Task<bool> ExecuteChangeAsync(string teamId, SyncChange change, int index, int totalOperations,
        IProgress<SyncProgress>? progress, List<SyncOperationResult> results, CancellationToken cancellationToken)
    {
        var targetObjectId = change.Kind == ChangeKind.Add ? change.UserId : change.MembershipId;
        _logger.LogInformation("MemberOperationStarted Kind={Kind} TargetObjectId={TargetObjectId}",
            change.Kind, targetObjectId);
        try
        {
            if (change.Kind == ChangeKind.Add)
                await teamsGateway.AddMemberAsync(teamId, change.UserId!, cancellationToken);
            else
                await teamsGateway.RemoveMemberAsync(teamId, change.MembershipId!, cancellationToken);
            results.Add(new SyncOperationResult(change.Kind, change.Email, true, null));
            _logger.LogInformation("MemberOperationSucceeded Kind={Kind} TargetObjectId={TargetObjectId}",
                change.Kind, targetObjectId);
            progress?.Report(new SyncProgress(index + 1, totalOperations, change.Kind, change.Email));
            if (index < totalOperations - 1)
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception ex)
        {
            results.Add(new SyncOperationResult(change.Kind, change.Email, false, ex.Message));
            _logger.LogWarning(
                "MemberOperationFailed Kind={Kind} TargetObjectId={TargetObjectId} ErrorType={ErrorType}",
                change.Kind, targetObjectId, ex.GetType().Name);
            return false;
        }
    }

    private void LogSyncCancelled(IReadOnlyList<SyncOperationResult> results)
    {
        _logger.LogWarning("SyncCancelled Success={SuccessCount} Failure={FailureCount}",
            results.Count(result => result.Succeeded), results.Count(result => !result.Succeeded));
    }

    private static bool PlansAreEquivalent(SyncPlan preview, SyncPlan latest)
    {
        if (preview.Mode != latest.Mode) return false;
        var previewSnapshot = preview.MembershipSnapshot ?? [];
        var latestSnapshot = latest.MembershipSnapshot ?? [];
        if (!previewSnapshot.SequenceEqual(latestSnapshot)) return false;

        static IEnumerable<(ChangeKind Kind, string? UserId, string? MembershipId)> Operations(
            SyncPlan plan)
        {
            return plan.Changes
                .Where(change => change.Kind is ChangeKind.Add or ChangeKind.Remove or
                    ChangeKind.Protected or ChangeKind.Error)
                .Select(change => (change.Kind, change.UserId, change.MembershipId))
                .OrderBy(change => change.Kind)
                .ThenBy(change => change.UserId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.MembershipId, StringComparer.OrdinalIgnoreCase);
        }

        return Operations(preview).SequenceEqual(Operations(latest));
    }
}
