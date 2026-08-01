using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class TeamSelectionViewModelTests
{
    [Fact]
    public async Task TeamSelection_読み込み後はチームを自動選択しない()
    {
        FakeTeamsGateway gateway = new()
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)]
        };
        TeamSelectionViewModel viewModel = new(gateway, new FailingNotificationService());

        await viewModel.InitializeAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Equal(2, viewModel.Teams.Count);
        Assert.Null(viewModel.SelectedTeam);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task TeamSelection_選択中に再読込しても同じチームが所有チームに残っていれば選択を維持する()
    {
        FakeTeamsGateway gateway = new()
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)]
        };
        TeamSelectionViewModel viewModel = new(gateway, new FailingNotificationService());
        await viewModel.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        viewModel.SelectedTeam = viewModel.Teams.Single(team => team.Id == "team-1");
        // 再読込後は新しいTeamInfoインスタンスが返る想定のため、同じIdだが別インスタンスにしておく。
        gateway.OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)];

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedTeam);
        Assert.Equal("team-1", viewModel.SelectedTeam.Id);
    }

    [Fact]
    public async Task TeamSelection_選択中のチームが再読込後に所有チームから無くなっていたら選択を解除する()
    {
        FakeTeamsGateway gateway = new()
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)]
        };
        TeamSelectionViewModel viewModel = new(gateway, new FailingNotificationService());
        await viewModel.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        viewModel.SelectedTeam = viewModel.Teams.Single(team => team.Id == "team-1");
        gateway.OwnedTeams = [new TeamInfo("team-2", "運用", null)];

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedTeam);
    }

    [Fact]
    public async Task TeamSelection_一致しない検索語でHasNoSearchResultsがtrueになる()
    {
        FakeTeamsGateway gateway = new()
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)]
        };
        TeamSelectionViewModel viewModel = new(gateway, new FailingNotificationService());
        await viewModel.InitializeAsync("current-user", TestContext.Current.CancellationToken);

        viewModel.SearchText = "存在しないチーム";

        Assert.True(viewModel.HasNoSearchResults);
    }

    [Theory]
    [InlineData("開発")]
    [InlineData("")]
    public async Task TeamSelection_一致する検索語または空文字ではHasNoSearchResultsがfalseになる(string searchText)
    {
        FakeTeamsGateway gateway = new()
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)]
        };
        TeamSelectionViewModel viewModel = new(gateway, new FailingNotificationService());
        await viewModel.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        viewModel.SearchText = searchText;

        Assert.False(viewModel.HasNoSearchResults);
    }

    [Fact]
    public void TeamSelection_チーム未読み込みの状態では検索語を入れてもHasNoSearchResultsはfalseのまま()
    {
        TeamSelectionViewModel viewModel = new(new FakeTeamsGateway(), new FailingNotificationService());

        viewModel.SearchText = "存在しないチーム";

        Assert.False(viewModel.HasNoSearchResults);
    }

    [Fact]
    public void TeamSelection_ClearSearchCommandを実行するとSearchTextが空になりHasSearchTextがfalseになる()
    {
        TeamSelectionViewModel viewModel =
            new(new FakeTeamsGateway(), new FailingNotificationService()) { SearchText = "開発" };
        Assert.True(viewModel.HasSearchText);
        Assert.True(viewModel.ClearSearchCommand.CanExecute(null));

        viewModel.ClearSearchCommand.Execute(null);

        Assert.Equal("", viewModel.SearchText);
        Assert.False(viewModel.HasSearchText);
    }

    [Fact]
    public void TeamSelection_PrepareSelectionCommandは検索をクリアしてフォーカス要求を通知する()
    {
        TeamSelectionViewModel viewModel =
            new(new FakeTeamsGateway(), new FailingNotificationService()) { SearchText = "開発" };
        bool requested = false;
        viewModel.SelectionFocusRequested += () => requested = true;

        viewModel.PrepareSelectionCommand.Execute(null);

        Assert.Equal("", viewModel.SearchText);
        Assert.True(requested);
    }
}