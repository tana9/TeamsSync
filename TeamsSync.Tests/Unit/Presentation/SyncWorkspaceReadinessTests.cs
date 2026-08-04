using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;
using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class SyncWorkspaceReadinessTests
{
    private static readonly TeamInfo Team = new("team-1", "開発", null);

    private static MemberListDocument Document()
    {
        return new MemberListDocument(["a@example.com"], "members.csv", "C:\\members.csv",
            new DateTime(2026, 7, 28), "members.csv", "email");
    }

    private static SyncPlan PlanWithAdd()
    {
        return new SyncPlan(Team, [new SyncChange(ChangeKind.Add, "新規", "new@example.com", ChangeReason.AddToTeam)],
            ["new@example.com"]);
    }

    private static SyncWorkspaceContext Context(bool signedIn, TeamInfo? team, MemberListDocument? document)
    {
        SyncWorkspaceContext context = new();
        context.Update(team, document, signedIn, null, null);
        return context;
    }

    // 旧BuildSyncUnavailableReason(bool isSyncing, bool isBusy, bool externallyBusy, ...)は
    // 呼び出し側でtrue/false/falseのように並べる必要があり取り違えやすかった。
    // 名前付き引数で意図を明示できることも、このモデルへ集約した効果の1つ
    private static SyncWorkspaceReadiness Readiness(SyncWorkspaceContext context, SyncPlan? plan,
        bool externallyBusy = false, bool previewBusy = false, bool executionBusy = false)
    {
        SyncPreviewDisplayState preview = new();
        preview.SetBusy(previewBusy);
        SyncExecutionDisplayState execution = new();
        execution.SetPreparing(executionBusy);
        return new SyncWorkspaceReadiness(externallyBusy, preview, execution, context, plan);
    }

    [Fact]
    public void UnavailableReason_同期実行中の場合はその旨を返す()
    {
        string text = Readiness(Context(true, Team, Document()), PlanWithAdd(), executionBusy: true).UnavailableReason;

        Assert.Equal("チームへ反映中です", text);
    }

    [Fact]
    public void UnavailableReason_自身がbusyの場合は待機を促す()
    {
        string text = Readiness(Context(true, Team, Document()), PlanWithAdd(), previewBusy: true).UnavailableReason;

        Assert.Equal("別の処理が完了するまでお待ちください", text);
    }

    [Fact]
    public void UnavailableReason_外部要因でbusyの場合は待機を促す()
    {
        string text =
            Readiness(Context(true, Team, Document()), PlanWithAdd(), externallyBusy: true).UnavailableReason;

        Assert.Equal("別の処理が完了するまでお待ちください", text);
    }

    [Fact]
    public void UnavailableReason_未サインインの場合はサインインを促す()
    {
        string text = Readiness(Context(false, Team, Document()), PlanWithAdd()).UnavailableReason;

        Assert.Equal("Microsoft 365へサインインしてください", text);
    }

    [Fact]
    public void UnavailableReason_チーム未選択の場合はチーム選択を促す()
    {
        string text = Readiness(Context(true, null, Document()), PlanWithAdd()).UnavailableReason;

        Assert.Equal("同期先のチームを選択してください", text);
    }

    [Fact]
    public void UnavailableReason_文書未選択の場合はメンバーリスト指定を促す()
    {
        string text = Readiness(Context(true, Team, null), PlanWithAdd()).UnavailableReason;

        Assert.Equal("メンバーリストを指定してください", text);
    }

    [Fact]
    public void UnavailableReason_プラン未確認の場合は差分確認を促す()
    {
        string text = Readiness(Context(true, Team, Document()), null).UnavailableReason;

        Assert.Equal("先に「差分を確認」を実行してください", text);
    }

    [Fact]
    public void UnavailableReason_プランにエラーがある場合は未解決ユーザーの修正を促す()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Error, "不明", "unknown@example.com", ChangeReason.UserNotFound)],
            ["unknown@example.com"]);

        string text = Readiness(Context(true, Team, Document()), plan).UnavailableReason;

        Assert.Equal("未解決ユーザーを修正してください", text);
    }

    [Fact]
    public void UnavailableReason_変更が0件の場合は実行する変更がない旨を返す()
    {
        SyncPlan plan = new(Team,
            [new SyncChange(ChangeKind.Keep, "既存", "keep@example.com", ChangeReason.AlreadyMember)],
            ["keep@example.com"]);

        string text = Readiness(Context(true, Team, Document()), plan).UnavailableReason;

        Assert.Equal("反映する変更はありません", text);
    }

    [Fact]
    public void UnavailableReason_実行可能な場合は実行可能メッセージを返す()
    {
        string text = Readiness(Context(true, Team, Document()), PlanWithAdd()).UnavailableReason;

        Assert.Equal("チームに反映できます", text);
    }

    [Fact]
    public void CanPreview_サインイン済みでチームと文書がある場合はtrue()
    {
        Assert.True(Readiness(Context(true, Team, Document()), null).CanPreview);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void CanPreview_未サインイン_チーム未選択_文書未選択のいずれかならfalse(bool signedIn, bool hasTeam,
        bool hasDocument)
    {
        SyncWorkspaceContext context = Context(signedIn, hasTeam ? Team : null, hasDocument ? Document() : null);

        Assert.False(Readiness(context, null).CanPreview);
    }

    [Fact]
    public void CanPreview_実行中や外部busyの場合はfalse()
    {
        SyncWorkspaceContext context = Context(true, Team, Document());

        Assert.False(Readiness(context, null, previewBusy: true).CanPreview);
        Assert.False(Readiness(context, null, executionBusy: true).CanPreview);
        Assert.False(Readiness(context, null, externallyBusy: true).CanPreview);
    }

    [Fact]
    public void CanExecuteSync_プランが実行可能な場合はtrue()
    {
        Assert.True(Readiness(Context(true, Team, Document()), PlanWithAdd()).CanExecuteSync);
    }

    [Fact]
    public void CanExecuteSync_プランがnullまたは実行不可の場合はfalse()
    {
        SyncWorkspaceContext context = Context(true, Team, Document());
        SyncPlan noActionPlan = new(Team,
            [new SyncChange(ChangeKind.Keep, "既存", "keep@example.com", ChangeReason.AlreadyMember)],
            ["keep@example.com"]);

        Assert.False(Readiness(context, null).CanExecuteSync);
        Assert.False(Readiness(context, noActionPlan).CanExecuteSync);
    }

    [Fact]
    public void CanExecuteSync_busyの場合はサインインやチーム未選択に関わらずfalse()
    {
        Assert.False(Readiness(Context(true, Team, Document()), PlanWithAdd(), previewBusy: true).CanExecuteSync);
    }
}