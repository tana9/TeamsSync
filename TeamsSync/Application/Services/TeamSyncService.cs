using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>
///     入力アドレス一覧と現メンバーを突き合わせて同期プランを作成し、そのプランに基づいて
///     メンバーの追加・削除を実行する。
/// </summary>
public sealed class TeamSyncService(ITeamsGateway teamsGateway, ILogger<TeamSyncService>? logger = null)
{
    private const int AddressResolutionConcurrency = 15;
    private const int ProgressReportInterval = 25;
    private readonly ILogger<TeamSyncService> _logger = logger ?? NullLogger<TeamSyncService>.Instance;

    /// <summary>
    ///     入力アドレス一覧を現メンバーおよびディレクトリと突き合わせて解決し、
    ///     追加・削除・維持・エラーの各変更内容をまとめた同期プランを作成する。
    /// </summary>
    public async Task<SyncPlan> BuildPlanAsync(TeamInfo team, IReadOnlyList<string> addresses,
        CancellationToken cancellationToken = default, SyncMode mode = SyncMode.FullSync,
        IProgress<int>? progress = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<TeamMember> current = await teamsGateway.GetTeamMembersAsync(team.Id, cancellationToken);
        CurrentMemberIndex currentIndex = new(current);
        AddressResolutionBatch resolutionBatch = await ResolveAddressesAsync(addresses, currentIndex, progress,
            cancellationToken);

        (Dictionary<string, DirectoryUser> resolved, List<SyncChange> changes) =
            ClassifyResolutions(resolutionBatch.Resolutions, mode);
        if (mode == SyncMode.FullSync)
        {
            changes.AddRange(ComputeRemovals(current, resolved, changes));
        }

        List<TeamMembershipSnapshot> snapshot = BuildMembershipSnapshot(current);
        SyncPlan plan = new(team, changes, addresses, current.Count(x => !x.IsOwner), snapshot, mode);
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
        CurrentMemberIndex current, IProgress<int>? progress, CancellationToken cancellationToken)
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
    ///     アドレスごとの解決結果を、追加・維持・保護・エラーの各<see cref="SyncChange" />へ分類する。
    ///     同一ユーザーが氏名とメールアドレスなど複数の表記で重複指定された場合、2件目以降を別行にはせず
    ///     最初の行のEmailへ表記を合流させて1行にまとめる。
    /// </summary>
    private static (Dictionary<string, DirectoryUser> Resolved, List<SyncChange> Changes) ClassifyResolutions(
        IReadOnlyList<AddressResolution> resolutions, SyncMode mode)
    {
        Dictionary<string, DirectoryUser> resolved = new(StringComparer.OrdinalIgnoreCase);
        List<SyncChange> changes = [];
        Dictionary<string, int> rowIndexByUserId = new(StringComparer.OrdinalIgnoreCase);

        void AddOrMergeChange(string address, string userId, string? membershipId, string displayName,
            ChangeKind kind, string detail)
        {
            if (rowIndexByUserId.TryGetValue(userId, out int index))
            {
                changes[index] = changes[index] with { Email = $"{changes[index].Email} ／ {address}" };
                return;
            }

            rowIndexByUserId[userId] = changes.Count;
            changes.Add(new SyncChange(kind, displayName, address, detail, userId, membershipId));
        }

        foreach (AddressResolution resolution in resolutions)
        {
            switch (resolution)
            {
                case { Outcome: ResolutionOutcome.Error }:
                    changes.Add(new SyncChange(ChangeKind.Error, "", resolution.Address,
                        resolution.ErrorMessage!));
                    break;
                case { Outcome: ResolutionOutcome.ExistingSingle, ExistingMember: { } existing }:
                    AddOrMergeChange(resolution.Address, existing.UserId, existing.MembershipId,
                        existing.DisplayName,
                        existing.IsOwner ? ChangeKind.Protected :
                        mode == SyncMode.RemoveSpecified ? ChangeKind.Remove : ChangeKind.Keep,
                        existing.IsOwner ? "所有者のため削除しません" :
                        mode == SyncMode.RemoveSpecified ? "指定された一般メンバーを削除します" : "既にメンバーです");
                    break;
                case
                {
                    Outcome: ResolutionOutcome.SameUserDifferentAddress, ExistingMember: { } sameUser,
                    User: { } sameUserAccount
                }:
                    resolved[resolution.Address] = sameUserAccount;
                    AddOrMergeChange(resolution.Address, sameUser.UserId, sameUser.MembershipId,
                        sameUser.DisplayName,
                        sameUser.IsOwner ? ChangeKind.Protected :
                        mode == SyncMode.RemoveSpecified ? ChangeKind.Remove : ChangeKind.Keep,
                        sameUser.IsOwner ? "所有者のため削除しません" :
                        mode == SyncMode.RemoveSpecified ? "指定された一般メンバーを削除します" :
                        "別のアドレスで既にメンバーです");
                    break;
                case { Outcome: ResolutionOutcome.NewUser, User: { } user }:
                    resolved[resolution.Address] = user;
                    AddOrMergeChange(resolution.Address, user.Id, null, user.DisplayName,
                        mode == SyncMode.RemoveSpecified ? ChangeKind.NotMember : ChangeKind.Add,
                        mode == SyncMode.RemoveSpecified ? "現在このチームに所属していません" : "メンバーに追加します");
                    break;
            }
        }

        return (resolved, changes);
    }

    /// <summary>
    ///     完全同期モード用に、入力リストに含まれない一般メンバー(所有者を除く)を
    ///     削除対象の<see cref="SyncChange" />として列挙する。
    /// </summary>
    private static IEnumerable<SyncChange> ComputeRemovals(IReadOnlyList<TeamMember> current,
        Dictionary<string, DirectoryUser> resolved, IReadOnlyList<SyncChange> changes)
    {
        HashSet<string> wantedIds = resolved.Values.Select(x => x.Id)
            .Concat(changes.Where(x => x.Kind is ChangeKind.Keep or ChangeKind.Protected)
                .Select(x => x.UserId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return current.Where(x => !x.IsOwner && !wantedIds.Contains(x.UserId))
            .Select(member => new SyncChange(ChangeKind.Remove, member.DisplayName, member.Email,
                "リストにないため削除します", member.UserId, member.MembershipId));
    }

    /// <summary>
    ///     再検証(<see cref="RevalidatePlanAsync" />)で使う、現メンバーシップのスナップショットを作成する。
    /// </summary>
    private static List<TeamMembershipSnapshot> BuildMembershipSnapshot(IReadOnlyList<TeamMember> current)
    {
        return current
            .Select(member => new TeamMembershipSnapshot(
                member.MembershipId, member.UserId, member.IsOwner))
            .OrderBy(member => member.MembershipId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.UserId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    ///     1件のアドレスを解決する。現メンバーの中に一致があればそれを優先し、
    ///     なければ同時実行数を制限しつつディレクトリ検索(<see cref="ITeamsGateway.FindUsersAsync" />)を行う。
    /// </summary>
    private async Task<AddressResolutionAttempt> ResolveAddressAsync(string address, CurrentMemberIndex current,
        SemaphoreSlim concurrency, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddressResolution? local = TryResolveFromCurrentMembers(address, current);
        if (local is not null)
        {
            return new AddressResolutionAttempt(local, false);
        }

        await concurrency.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<DirectoryUser> candidates = await teamsGateway.FindUsersAsync(address, cancellationToken);
            return new AddressResolutionAttempt(ResolveFromCandidates(address, candidates, current), true);
        }
        finally
        {
            concurrency.Release();
        }
    }

    /// <summary>
    ///     メールアドレスまたは正規化した氏名で現メンバーから一致を探す。
    ///     複数一致した場合は特定不能としてエラーにする。
    /// </summary>
    private static AddressResolution? TryResolveFromCurrentMembers(string address, CurrentMemberIndex current)
    {
        IReadOnlyList<TeamMember> existingMatches = current.FindByAddressOrName(address);
        if (existingMatches.Count > 1)
        {
            return new AddressResolution(ResolutionOutcome.Error, address,
                ErrorMessage: "同じ氏名のチームメンバーが複数いるため特定できません");
        }

        return existingMatches.Count == 1
            ? new AddressResolution(ResolutionOutcome.ExistingSingle, address, existingMatches[0])
            : null;
    }

    /// <summary>
    ///     ディレクトリ検索の候補から一意なユーザーを特定し、既に別アドレスでメンバーかどうかを判定する。
    /// </summary>
    private static AddressResolution ResolveFromCandidates(string address,
        IReadOnlyList<DirectoryUser> candidates, CurrentMemberIndex current)
    {
        List<DirectoryUser> exactMatches = candidates
            .DistinctBy(user => user.Id, StringComparer.OrdinalIgnoreCase).ToList();
        if (exactMatches.Count != 1)
        {
            return new AddressResolution(ResolutionOutcome.Error, address,
                ErrorMessage: exactMatches.Count > 1
                    ? "同じ氏名のユーザーが複数いるため、メールアドレスで指定してください"
                    : "ユーザーが見つかりません");
        }

        DirectoryUser user = exactMatches[0];
        TeamMember? sameUser = current.FindByUserId(user.Id);
        return sameUser is not null
            ? new AddressResolution(ResolutionOutcome.SameUserDifferentAddress, address,
                sameUser, user)
            : new AddressResolution(ResolutionOutcome.NewUser, address, User: user);
    }

    /// <summary>現在メンバーをメールアドレス・正規化氏名・ユーザーIDで定数時間検索する索引。</summary>
    private sealed class CurrentMemberIndex
    {
        private readonly Dictionary<string, List<TeamMember>> _byEmail = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<TeamMember>> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TeamMember> _byUserId = new(StringComparer.OrdinalIgnoreCase);

        public CurrentMemberIndex(IEnumerable<TeamMember> members)
        {
            foreach (TeamMember member in members)
            {
                Add(_byEmail, member.Email, member);
                Add(_byName, UserIdentifier.NormalizeName(member.DisplayName), member);
                _byUserId.TryAdd(member.UserId, member);
            }
        }

        public IReadOnlyList<TeamMember> FindByAddressOrName(string value)
        {
            List<TeamMember> matches = [];
            if (_byEmail.TryGetValue(value, out List<TeamMember>? emailMatches))
            {
                matches.AddRange(emailMatches);
            }

            string normalizedName = UserIdentifier.NormalizeName(value);
            if (_byName.TryGetValue(normalizedName, out List<TeamMember>? nameMatches))
            {
                foreach (TeamMember member in nameMatches)
                {
                    if (!matches.Any(match => string.Equals(match.MembershipId, member.MembershipId,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        matches.Add(member);
                    }
                }
            }

            return matches;
        }

        public TeamMember? FindByUserId(string userId)
        {
            return _byUserId.GetValueOrDefault(userId);
        }

        private static void Add(Dictionary<string, List<TeamMember>> index, string key, TeamMember member)
        {
            if (!index.TryGetValue(key, out List<TeamMember>? matches))
            {
                matches = [];
                index.Add(key, matches);
            }

            matches.Add(member);
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
            preview.Team, preview.InputAddresses, cancellationToken, preview.Mode);
        return new SyncPlanRevalidation(PlansAreEquivalent(preview, latest), latest);
    }

    /// <summary>
    ///     実行済みプランと同じチーム・入力アドレスに対して、実行後の実際の状態を反映した
    ///     新しいプランを作成する(実行結果の確認用)。
    /// </summary>
    public Task<SyncPlan> ReconcileAsync(SyncPlan executedPlan,
        CancellationToken cancellationToken = default)
    {
        return BuildPlanAsync(executedPlan.Team, executedPlan.InputAddresses,
            cancellationToken, executedPlan.Mode);
    }

    /// <summary>
    ///     同期プランに含まれる追加・削除操作を順に実行する。未解決の変更がある場合、
    ///     または追加のみモードで削除が含まれる場合は例外をスローする。
    /// </summary>
    public async Task<SyncExecutionResult> ExecuteAsync(SyncPlan plan,
        IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default,
        SyncAuditContext? auditContext = null)
    {
        ValidateExecutablePlan(plan);
        using IDisposable? auditScope = BeginAuditScope(plan, auditContext);
        return await ExecuteOperationsAsync(plan, progress, cancellationToken);
    }

    private static void ValidateExecutablePlan(SyncPlan plan)
    {
        if (plan.HasErrors)
        {
            throw new InvalidOperationException("未解決ユーザーがあります。同期は実行できません。");
        }

        if (plan.Mode == SyncMode.AddOnly && plan.RemoveCount > 0)
        {
            throw new InvalidOperationException("追加のみモードではメンバーを削除できません。");
        }

        if (plan.Mode == SyncMode.RemoveSpecified && plan.AddCount > 0)
        {
            throw new InvalidOperationException("指定メンバー削除モードではメンバーを追加できません。");
        }
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
        List<SyncChange> operations = plan.Changes.Where(x => x.Kind is ChangeKind.Add or ChangeKind.Remove).ToList();
        _logger.LogInformation(
            "SyncStarted Add={AddCount} Remove={RemoveCount} Keep={KeepCount} Protected={ProtectedCount}",
            plan.AddCount, plan.RemoveCount, plan.KeepCount, plan.ProtectedCount);
        for (int i = 0; i < operations.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                LogSyncCancelled(results, operations.Count);
                return new SyncExecutionResult(results, true);
            }

            SyncChange change = operations[i];
            progress?.Report(new SyncProgress(i, operations.Count, change.Kind, change.Email));
            bool cancelled = await ExecuteChangeAsync(plan.Team.Id, change, i, operations.Count, progress, results,
                cancellationToken);
            if (cancelled)
            {
                LogSyncCancelled(results, operations.Count);
                return new SyncExecutionResult(results, true);
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
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
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

    /// <summary>
    ///     2つのプランが、同期モード・メンバーシップのスナップショット・
    ///     追加/削除/保護/エラーの各操作内容の点で同一かどうかを判定する。
    /// </summary>
    private static bool PlansAreEquivalent(SyncPlan preview, SyncPlan latest)
    {
        if (preview.Mode != latest.Mode)
        {
            return false;
        }

        IReadOnlyList<TeamMembershipSnapshot> previewSnapshot = preview.MembershipSnapshot ?? [];
        IReadOnlyList<TeamMembershipSnapshot> latestSnapshot = latest.MembershipSnapshot ?? [];
        if (!previewSnapshot.SequenceEqual(latestSnapshot))
        {
            return false;
        }

        static IEnumerable<(ChangeKind Kind, string? UserId, string? MembershipId)> Operations(
            SyncPlan plan)
        {
            return plan.Changes
                .Where(change => change.Kind is ChangeKind.Add or ChangeKind.Remove or
                    ChangeKind.Protected or ChangeKind.NotMember or ChangeKind.Error)
                .Select(change => (change.Kind, change.UserId, change.MembershipId))
                .OrderBy(change => change.Kind)
                .ThenBy(change => change.UserId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.MembershipId, StringComparer.OrdinalIgnoreCase);
        }

        return Operations(preview).SequenceEqual(Operations(latest));
    }

    /// <summary>1件のアドレス解決の結果種別。</summary>
    private enum ResolutionOutcome
    {
        /// <summary>解決に失敗した。</summary>
        Error,

        /// <summary>現メンバーの中に一意な一致があった。</summary>
        ExistingSingle,

        /// <summary>ディレクトリ検索で一致した、現メンバーにいない新規ユーザー。</summary>
        NewUser,

        /// <summary>ディレクトリ検索で一致したユーザーが、別のアドレスで既に現メンバーだった。</summary>
        SameUserDifferentAddress
    }

    /// <summary>1件のアドレス解決結果を保持する内部データ。</summary>
    private sealed record AddressResolution(
        ResolutionOutcome Outcome,
        string Address,
        TeamMember? ExistingMember = null,
        DirectoryUser? User = null,
        string? ErrorMessage = null);

    private sealed record AddressResolutionAttempt(AddressResolution Resolution, bool DirectorySearched);

    private sealed record AddressResolutionGroup(string Address, AddressResolutionAttempt Attempt);

    private sealed record AddressResolutionBatch(
        AddressResolution[] Resolutions, int UniqueInputCount, int DirectorySearchCount);
}
