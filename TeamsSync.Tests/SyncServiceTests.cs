using Microsoft.Extensions.Logging;
using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Tests;

public sealed class SyncServiceTests
{
    private static readonly TeamInfo Team = new("team-1", "開発チーム", null);

    [Fact]
    public async Task BuildPlan_ProtectsEveryOwnerEvenWhenMissingFromInput()
    {
        var graph = new FakeGraphService
        {
            Members =
            [
                Member("owner-membership", "owner-id", "Owner", "owner@example.com", true),
                Member("member-membership", "member-id", "Member", "member@example.com")
            ]
        };
        var plan = await new TeamSyncService(graph).BuildPlanAsync(Team, ["member@example.com"],
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(plan.Changes, x => x.Kind == ChangeKind.Remove && x.UserId == "owner-id");
        Assert.Contains(plan.Changes, x => x.Kind == ChangeKind.Keep && x.UserId == "member-id");
        Assert.Equal(0, plan.RemoveCount);
    }

    [Fact]
    public async Task BuildPlan_AddsListedUserAndRemovesOnlyUnlistedNonOwner()
    {
        var graph = new FakeGraphService
        {
            Members =
            [
                Member("owner-membership", "owner-id", "Owner", "owner@example.com", true),
                Member("old-membership", "old-id", "Old", "old@example.com")
            ]
        };
        graph.Users["new@example.com"] =
            new DirectoryUser("new-id", "New User", "new@example.com", "new@example.com");
        var plan = await new TeamSyncService(graph).BuildPlanAsync(Team, ["new@example.com"],
            TestContext.Current.CancellationToken);
        var added = Assert.Single(plan.Changes, x => x.Kind == ChangeKind.Add);
        Assert.Equal("new-id", added.UserId);
        var removed = Assert.Single(plan.Changes, x => x.Kind == ChangeKind.Remove);
        Assert.Equal("old-membership", removed.MembershipId);
    }

    [Fact]
    public async Task BuildPlan_AddOnlyNeverCreatesRemoval()
    {
        var graph = new FakeGraphService
        {
            Members = [Member("old-membership", "old-id", "Old", "old@example.com")]
        };
        graph.Users["new@example.com"] =
            new DirectoryUser("new-id", "New", "new@example.com", "new@example.com");

        var plan = await new TeamSyncService(graph).BuildPlanAsync(Team, ["new@example.com"],
            TestContext.Current.CancellationToken, SyncMode.AddOnly);

        Assert.Equal(SyncMode.AddOnly, plan.Mode);
        Assert.Equal(1, plan.AddCount);
        Assert.Equal(0, plan.RemoveCount);
    }

    [Fact]
    public async Task BuildPlan_RemoveSpecifiedDeletesOnlyListedNonOwners()
    {
        var graph = new FakeGraphService
        {
            Members =
            [
                Member("owner-membership", "owner-id", "Owner", "owner@example.com", true),
                Member("first-membership", "first-id", "First", "first@example.com"),
                Member("second-membership", "second-id", "Second", "second@example.com"),
                Member("other-membership", "other-id", "Other", "other@example.com")
            ]
        };

        var plan = await new TeamSyncService(graph).BuildPlanAsync(Team,
            ["first@example.com", "second@example.com", "owner@example.com"],
            TestContext.Current.CancellationToken, SyncMode.RemoveSpecified);

        Assert.Equal(SyncMode.RemoveSpecified, plan.Mode);
        Assert.Equal(2, plan.RemoveCount);
        Assert.Equal(1, plan.ProtectedCount);
        Assert.Equal(0, plan.AddCount);
        Assert.DoesNotContain(plan.Changes, change => change.UserId == "other-id");

        await new TeamSyncService(graph).ExecuteAsync(plan,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            [("team-1", "first-membership"), ("team-1", "second-membership")],
            graph.Removed);
        Assert.Empty(graph.Added);
    }

    [Fact]
    public async Task BuildPlan_RemoveSpecifiedMarksDirectoryUserOutsideTeamAsNotMember()
    {
        var graph = new FakeGraphService();
        graph.Users["outside@example.com"] =
            new DirectoryUser("outside-id", "Outside", "outside@example.com", "outside@example.com");

        var plan = await new TeamSyncService(graph).BuildPlanAsync(Team, ["outside@example.com"],
            TestContext.Current.CancellationToken, SyncMode.RemoveSpecified);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.NotMember, change.Kind);
        Assert.Equal(1, plan.NotMemberCount);
        Assert.False(plan.HasErrors);
        Assert.Equal(0, plan.RemoveCount);
        Assert.Equal(0, plan.AddCount);
    }

    [Fact]
    public async Task Execute_RejectsAdditionEmbeddedInRemoveSpecifiedPlan()
    {
        var graph = new FakeGraphService();
        var plan = new SyncPlan(Team,
            [new SyncChange(ChangeKind.Add, "New", "new@example.com", "", "new-id")],
            [], Mode: SyncMode.RemoveSpecified);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TeamSyncService(graph).ExecuteAsync(plan,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(graph.Added);
        Assert.Empty(graph.Removed);
    }

    [Fact]
    public async Task BuildPlan_アドレス解決の進捗を1件ごとに報告する()
    {
        var graph = new FakeGraphService
        {
            Members = [Member("keep-membership", "keep-id", "Keep", "keep@example.com")]
        };
        graph.Users["new1@example.com"] =
            new DirectoryUser("new1-id", "New1", "new1@example.com", "new1@example.com");
        graph.Users["new2@example.com"] =
            new DirectoryUser("new2-id", "New2", "new2@example.com", "new2@example.com");
        var reported = new List<int>();
        var progress = new SynchronousProgress<int>(value =>
        {
            lock (reported) reported.Add(value);
        });

        await new TeamSyncService(graph).BuildPlanAsync(Team,
            ["keep@example.com", "new1@example.com", "new2@example.com"],
            TestContext.Current.CancellationToken, progress: progress);

        Assert.Equal(3, reported.Count);
        Assert.Equal(3, reported.Max());
        Assert.Equal([1, 2, 3], reported.OrderBy(x => x));
    }

    [Fact]
    public async Task Execute_RejectsRemovalEmbeddedInAddOnlyPlan()
    {
        var graph = new FakeGraphService();
        var plan = new SyncPlan(Team,
            [new SyncChange(ChangeKind.Remove, "Old", "old@example.com", "", "old-id", "membership")],
            [], Mode: SyncMode.AddOnly);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TeamSyncService(graph).ExecuteAsync(plan,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(graph.Removed);
    }

    [Fact]
    public async Task BuildPlan_MarksUnknownUserAsError()
    {
        var plan = await new TeamSyncService(new FakeGraphService())
            .BuildPlanAsync(Team, ["missing@example.com"], TestContext.Current.CancellationToken);
        Assert.True(plan.HasErrors);
        Assert.Contains(plan.Changes, x => x.Kind == ChangeKind.Error && x.Email == "missing@example.com");
    }

    [Fact]
    public async Task BuildPlan_MatchesSameUserByIdWhenEmailDiffers()
    {
        var graph = new FakeGraphService
        {
            Members = [Member("membership-1", "user-1", "User", "primary@example.com")]
        };
        graph.Users["alias@example.com"] =
            new DirectoryUser("user-1", "User", "primary@example.com", "primary@example.com");
        var plan = await new TeamSyncService(graph).BuildPlanAsync(Team, ["alias@example.com"],
            TestContext.Current.CancellationToken);
        Assert.Contains(plan.Changes, x => x.Kind == ChangeKind.Keep && x.UserId == "user-1");
        Assert.Equal(0, plan.AddCount);
        Assert.Equal(0, plan.RemoveCount);
    }

    [Fact]
    public async Task BuildPlan_同じユーザーを別アドレスで指定すると1行にまとめられる()
    {
        var graph = new FakeGraphService();
        var user = new DirectoryUser("USER-1", "User", "user@example.com", "alias@example.com");
        graph.Users["user@example.com"] = user;
        graph.Users["alias@example.com"] = user;

        var plan = await new TeamSyncService(graph).BuildPlanAsync(
            Team, ["user@example.com", "alias@example.com"], TestContext.Current.CancellationToken);

        Assert.Equal(1, plan.AddCount);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(ChangeKind.Add, change.Kind);
        Assert.Equal("user@example.com ／ alias@example.com", change.Email);

        await new TeamSyncService(graph).ExecuteAsync(plan,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([("team-1", "USER-1")], graph.Added);
    }

    [Fact]
    public async Task BuildPlan_氏名とメールが同じユーザーなら1行にまとめられる()
    {
        var graph = new FakeGraphService();
        var user = new DirectoryUser("user-1", "山田 太郎", "taro@example.com", "taro@example.com");
        graph.Users["山田 太郎"] = user;
        graph.Users["taro@example.com"] = user;

        var plan = await new TeamSyncService(graph).BuildPlanAsync(
            Team, ["山田 太郎", "taro@example.com"], TestContext.Current.CancellationToken);

        Assert.Equal(1, plan.AddCount);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("山田 太郎 ／ taro@example.com", change.Email);
    }

    [Fact]
    public async Task BuildPlan_既存メンバーとのユーザーID比較は大文字小文字を区別しない()
    {
        var graph = new FakeGraphService
        {
            Members = [Member("membership-1", "user-1", "User", "primary@example.com")]
        };
        graph.Users["alias@example.com"] =
            new DirectoryUser("USER-1", "User", "primary@example.com", "primary@example.com");

        var plan = await new TeamSyncService(graph).BuildPlanAsync(
            Team, ["alias@example.com"], TestContext.Current.CancellationToken);

        Assert.Equal(0, plan.AddCount);
        Assert.Single(plan.Changes, change => change.Kind == ChangeKind.Keep);
    }

    [Fact]
    public async Task BuildPlan_MatchesExistingMemberNameIgnoringWhitespace()
    {
        var graph = new FakeGraphService
        {
            Members = [Member("membership-1", "user-1", "山田　太郎", "yamada@example.com")]
        };

        var plan = await new TeamSyncService(graph).BuildPlanAsync(
            Team, ["山田太郎"], TestContext.Current.CancellationToken);

        Assert.Contains(plan.Changes, change => change.Kind == ChangeKind.Keep && change.UserId == "user-1");
        Assert.Equal(0, plan.AddCount);
        Assert.Equal(0, plan.RemoveCount);
    }

    [Fact]
    public async Task BuildPlan_AddsDirectoryUserByNameIgnoringWhitespace()
    {
        var graph = new FakeGraphService();
        graph.SearchResults["山田太郎"] =
            [new DirectoryUser("user-1", "山田 太郎", "yamada@example.com", "yamada@example.com")];

        var plan = await new TeamSyncService(graph).BuildPlanAsync(
            Team, ["山田太郎"], TestContext.Current.CancellationToken);

        var addition = Assert.Single(plan.Changes, change => change.Kind == ChangeKind.Add);
        Assert.Equal("user-1", addition.UserId);
    }

    [Fact]
    public async Task BuildPlan_複数の未解決アドレスを並列解決しても入力順を維持する()
    {
        var graph = new FakeGraphService();
        graph.Users["alice@example.com"] =
            new DirectoryUser("alice-id", "Alice", "alice@example.com", "alice@example.com");
        graph.Users["bob@example.com"] =
            new DirectoryUser("bob-id", "Bob", "bob@example.com", "bob@example.com");
        graph.Users["carol@example.com"] =
            new DirectoryUser("carol-id", "Carol", "carol@example.com", "carol@example.com");

        var plan = await new TeamSyncService(graph).BuildPlanAsync(
            Team, ["alice@example.com", "bob@example.com", "carol@example.com"],
            TestContext.Current.CancellationToken);

        Assert.Equal(3, plan.AddCount);
        Assert.Equal(
            ["alice@example.com", "bob@example.com", "carol@example.com"],
            plan.Changes.Where(x => x.Kind == ChangeKind.Add).Select(x => x.Email));
    }

    [Fact]
    public async Task BuildPlan_RejectsAmbiguousDisplayName()
    {
        var graph = new FakeGraphService();
        graph.SearchResults["山田太郎"] =
        [
            new DirectoryUser("user-1", "山田 太郎", "one@example.com", "one@example.com"),
            new DirectoryUser("user-2", "山田　太郎", "two@example.com", "two@example.com")
        ];

        var plan = await new TeamSyncService(graph).BuildPlanAsync(
            Team, ["山田太郎"], TestContext.Current.CancellationToken);

        var error = Assert.Single(plan.Changes, change => change.Kind == ChangeKind.Error);
        Assert.Contains("複数", error.Detail);
        Assert.Equal(0, plan.AddCount);
    }

    [Fact]
    public async Task RevalidatePlan_ReturnsCurrentWhenMembershipAndOperationsAreUnchanged()
    {
        var graph = new FakeGraphService
        {
            Members = [Member("membership-1", "user-1", "User", "user@example.com")]
        };
        var service = new TeamSyncService(graph);
        var preview = await service.BuildPlanAsync(
            Team, ["user@example.com"], TestContext.Current.CancellationToken);

        var result = await service.RevalidatePlanAsync(
            preview, TestContext.Current.CancellationToken);

        Assert.True(result.IsCurrent);
    }

    [Fact]
    public async Task RevalidatePlan_DetectsOwnerPromotionAndRemovesStaleDeletion()
    {
        var graph = new FakeGraphService
        {
            Members = [Member("old-membership", "old-user", "Old", "old@example.com")]
        };
        graph.Users["new@example.com"] =
            new DirectoryUser("new-user", "New", "new@example.com", "new@example.com");
        var service = new TeamSyncService(graph);
        var preview = await service.BuildPlanAsync(
            Team, ["new@example.com"], TestContext.Current.CancellationToken);
        Assert.Equal(1, preview.RemoveCount);
        graph.Members = [Member("old-membership", "old-user", "Old", "old@example.com", true)];

        var result = await service.RevalidatePlanAsync(
            preview, TestContext.Current.CancellationToken);

        Assert.False(result.IsCurrent);
        Assert.Equal(0, result.LatestPlan.RemoveCount);
        Assert.DoesNotContain(result.LatestPlan.Changes,
            change => change.Kind == ChangeKind.Remove && change.UserId == "old-user");
    }

    [Fact]
    public async Task Execute_RejectsPlanContainingErrorsWithoutChangingTeam()
    {
        var graph = new FakeGraphService();
        var plan = new SyncPlan(Team,
            [new SyncChange(ChangeKind.Error, "", "missing@example.com", "not found")],
            ["missing@example.com"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TeamSyncService(graph).ExecuteAsync(plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(graph.Added);
        Assert.Empty(graph.Removed);
    }

    [Fact]
    public async Task Execute_PerformsAddsAndRemoves()
    {
        var graph = new FakeGraphService();
        var plan = new SyncPlan(Team,
        [
            new SyncChange(ChangeKind.Add, "New", "new@example.com", "", "new-id"),
            new SyncChange(ChangeKind.Remove, "Old", "old@example.com", "", "old-id", "old-membership")
        ], ["new@example.com"]);
        var result =
            await new TeamSyncService(graph).ExecuteAsync(plan,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal([("team-1", "new-id")], graph.Added);
        Assert.Equal([("team-1", "old-membership")], graph.Removed);
    }

    [Fact]
    public async Task Execute_ContinuesAfterOneOperationFailsAndReturnsPartialResult()
    {
        var graph = new FakeGraphService
        {
            OnAdd = _ => Task.FromException(new InvalidOperationException("add failed"))
        };
        var plan = new SyncPlan(Team,
        [
            new SyncChange(ChangeKind.Add, "New", "new@example.com", "", "new-id"),
            new SyncChange(ChangeKind.Remove, "Old", "old@example.com", "", "old-id", "old-membership")
        ], ["new@example.com"]);

        var result = await new TeamSyncService(graph).ExecuteAsync(
            plan, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal([("team-1", "old-membership")], graph.Removed);
    }

    [Fact]
    public async Task Reconcile_部分成功後は未反映の操作だけを計画する()
    {
        var graph = new FakeGraphService
        {
            Members =
            [
                Member("owner-membership", "owner-id", "Owner", "owner@example.com", true),
                Member("old-membership", "old-id", "Old", "old@example.com")
            ],
            OnRemove = _ => Task.FromException(new InvalidOperationException("remove failed"))
        };
        graph.Users["new@example.com"] =
            new DirectoryUser("new-id", "New", "new@example.com", "new@example.com");
        graph.OnAdd = _ =>
        {
            graph.Members =
            [
                .. graph.Members,
                Member("new-membership", "new-id", "New", "new@example.com")
            ];
            return Task.CompletedTask;
        };
        var service = new TeamSyncService(graph);
        var plan = await service.BuildPlanAsync(Team, ["new@example.com"],
            TestContext.Current.CancellationToken);

        var result = await service.ExecuteAsync(plan,
            cancellationToken: TestContext.Current.CancellationToken);
        var remaining = await service.ReconcileAsync(plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(0, remaining.AddCount);
        Assert.Equal(1, remaining.RemoveCount);
        Assert.Equal("old-membership", Assert.Single(remaining.Changes,
            change => change.Kind == ChangeKind.Remove).MembershipId);
    }

    [Fact]
    public async Task Reconcile_応答失敗でもGraphに反映済みなら二重追加しない()
    {
        var graph = new FakeGraphService
        {
            Members = [Member("owner-membership", "owner-id", "Owner", "owner@example.com", true)]
        };
        graph.Users["new@example.com"] =
            new DirectoryUser("new-id", "New", "new@example.com", "new@example.com");
        graph.OnAdd = _ =>
        {
            graph.Members =
            [
                .. graph.Members,
                Member("new-membership", "new-id", "New", "new@example.com")
            ];
            return Task.FromException(new InvalidOperationException("response lost"));
        };
        var service = new TeamSyncService(graph);
        var plan = await service.BuildPlanAsync(Team, ["new@example.com"],
            TestContext.Current.CancellationToken);

        var result = await service.ExecuteAsync(plan,
            cancellationToken: TestContext.Current.CancellationToken);
        var remaining = await service.ReconcileAsync(plan,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.FailureCount);
        Assert.Equal(0, remaining.AddCount);
        Assert.Contains(remaining.Changes,
            change => change.Kind == ChangeKind.Keep && change.UserId == "new-id");
    }

    [Fact]
    public async Task Execute_ReturnsCancelledBeforeChangingTeam()
    {
        var graph = new FakeGraphService();
        var plan = new SyncPlan(Team,
            [new SyncChange(ChangeKind.Add, "New", "new@example.com", "", "new-id")],
            ["new@example.com"]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new TeamSyncService(graph).ExecuteAsync(plan, cancellationToken: cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.Empty(graph.Added);
    }

    [Fact]
    public async Task Execute_CancelsInFlightGatewayOperation()
    {
        var graph = new FakeGraphService
        {
            OnAdd = token => Task.Delay(Timeout.InfiniteTimeSpan, token)
        };
        var plan = new SyncPlan(Team,
            [new SyncChange(ChangeKind.Add, "New", "new@example.com", "", "new-id")],
            ["new@example.com"]);
        using var cancellation = new CancellationTokenSource();

        var execution = new TeamSyncService(graph).ExecuteAsync(plan, cancellationToken: cancellation.Token);
        await Task.Yield();
        cancellation.Cancel();
        var result = await execution;

        Assert.True(result.Cancelled);
        Assert.Single(graph.Added);
    }

    [Fact]
    public async Task Execute_AuditEventsAreCorrelatedAndExcludeSensitiveValues()
    {
        var logger = new CapturingLogger<TeamSyncService>();
        var graph = new FakeGraphService
        {
            OnAdd = _ => Task.FromException(new InvalidOperationException(
                "SECRET_TOKEN sensitive.user@example.com C:\\DO_NOT_LOG_PATH"))
        };
        var plan = new SyncPlan(Team,
            [new SyncChange(ChangeKind.Add, "Sensitive User", "sensitive.user@example.com", "", "user-id")],
            ["sensitive.user@example.com"]);
        var executionId = Guid.Parse("0198a6ec-5f70-7000-8000-000000000001");
        var audit = new SyncAuditContext(executionId, "members.csv", new string('A', 64),
            "tenant-id", "actor-object-id");

        var result = await new TeamSyncService(graph, logger).ExecuteAsync(
            plan, cancellationToken: TestContext.Current.CancellationToken, auditContext: audit);

        Assert.Equal(1, result.FailureCount);
        Assert.Contains(logger.Records, record => record.Message.Contains("SyncStarted"));
        Assert.Contains(logger.Records, record => record.Message.Contains("MemberOperationFailed"));
        Assert.Contains(logger.Records, record => record.Message.Contains("SyncCompleted"));
        var scope = Assert.Single(logger.Scopes);
        Assert.Equal(executionId, scope["ExecutionId"]);
        Assert.Equal("team-1", scope["TeamId"]);
        Assert.Equal("members.csv", scope["InputFileName"]);
        Assert.Equal(new string('A', 64), scope["InputFileSha256"]);
        Assert.Equal("tenant-id", scope["TenantId"]);
        Assert.Equal("actor-object-id", scope["ActorObjectId"]);
        var auditText = string.Join(" ", logger.Records.SelectMany(record =>
            record.Properties.Select(property => property.Value?.ToString() ?? "").Append(record.Message)));
        Assert.DoesNotContain("sensitive.user@example.com", auditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET_TOKEN", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("DO_NOT_LOG_PATH", auditText, StringComparison.Ordinal);
    }

    private static TeamMember Member(string membershipId, string userId, string name, string email, bool owner = false)
    {
        return new TeamMember(membershipId, userId, name, email, owner);
    }

    private sealed record LogRecord(string Message, IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogRecord> Records { get; } = [];
        public List<IReadOnlyDictionary<string, object?>> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(ToDictionary(state));
            return NoopDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Records.Add(new LogRecord(formatter(state, exception), ToDictionary(state)));
        }

        private static IReadOnlyDictionary<string, object?> ToDictionary<TState>(TState state)
        {
            return state is IEnumerable<KeyValuePair<string, object?>> properties
                ? properties.ToDictionary(property => property.Key, property => property.Value)
                : new Dictionary<string, object?> { ["State"] = state };
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }

    private sealed class FakeGraphService : ITeamsGateway
    {
        public IReadOnlyList<TeamMember> Members { get; set; } = [];
        public Dictionary<string, DirectoryUser> Users { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<DirectoryUser>> SearchResults { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<(string TeamId, string UserId)> Added { get; } = [];
        public List<(string TeamId, string MembershipId)> Removed { get; } = [];
        public Func<CancellationToken, Task>? OnAdd { get; set; }
        public Func<CancellationToken, Task>? OnRemove { get; set; }

        public Task<(string Id, string DisplayName, string UserPrincipalName)> GetMeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(("current-user", "Current User", "current@example.com"));
        }

        public Task<IReadOnlyList<TeamInfo>> GetOwnedTeamsAsync(string currentUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TeamInfo>>([]);
        }

        public Task<IReadOnlyList<TeamMember>> GetTeamMembersAsync(string teamId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Members);
        }

        public Task<IReadOnlyList<DirectoryUser>> FindUsersAsync(string identifier,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SearchResults.TryGetValue(identifier, out var users) ? users :
                Users.TryGetValue(identifier, out var user) ? (IReadOnlyList<DirectoryUser>)[user] : []);
        }

        public Task AddMemberAsync(string teamId, string userId, CancellationToken cancellationToken = default)
        {
            Added.Add((teamId, userId));
            return OnAdd?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public Task RemoveMemberAsync(string teamId, string membershipId, CancellationToken cancellationToken = default)
        {
            Removed.Add((teamId, membershipId));
            return OnRemove?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }
}
