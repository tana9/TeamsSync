using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Tests;

public sealed class TeamSelectionViewModelTests
{
    [Fact]
    public async Task TeamSelection_読み込み後はチームを自動選択しない()
    {
        var gateway = new FakeTeamsGateway
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)]
        };
        var viewModel = new TeamSelectionViewModel(gateway, new FakeDialogs());

        await viewModel.InitializeAsync("current-user", TestContext.Current.CancellationToken);

        Assert.Equal(2, viewModel.Teams.Count);
        Assert.Null(viewModel.SelectedTeam);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task TeamSelection_一致しない検索語でHasNoSearchResultsがtrueになる()
    {
        var gateway = new FakeTeamsGateway
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)]
        };
        var viewModel = new TeamSelectionViewModel(gateway, new FakeDialogs());
        await viewModel.InitializeAsync("current-user", TestContext.Current.CancellationToken);

        viewModel.SearchText = "存在しないチーム";

        Assert.True(viewModel.HasNoSearchResults);
    }

    [Fact]
    public async Task TeamSelection_一致する検索語または空文字に戻すとHasNoSearchResultsがfalseに戻る()
    {
        var gateway = new FakeTeamsGateway
        {
            OwnedTeams = [new TeamInfo("team-1", "開発", null), new TeamInfo("team-2", "運用", null)]
        };
        var viewModel = new TeamSelectionViewModel(gateway, new FakeDialogs());
        await viewModel.InitializeAsync("current-user", TestContext.Current.CancellationToken);
        viewModel.SearchText = "存在しないチーム";
        Assert.True(viewModel.HasNoSearchResults);

        viewModel.SearchText = "開発";
        Assert.False(viewModel.HasNoSearchResults);

        viewModel.SearchText = "存在しないチーム";
        Assert.True(viewModel.HasNoSearchResults);

        viewModel.SearchText = "";
        Assert.False(viewModel.HasNoSearchResults);
    }

    [Fact]
    public void TeamSelection_チーム未読み込みの状態では検索語を入れてもHasNoSearchResultsはfalseのまま()
    {
        var viewModel = new TeamSelectionViewModel(new FakeTeamsGateway(), new FakeDialogs());

        viewModel.SearchText = "存在しないチーム";

        Assert.False(viewModel.HasNoSearchResults);
    }

    [Fact]
    public void TeamSelection_ClearSearchCommandを実行するとSearchTextが空になりHasSearchTextがfalseになる()
    {
        var viewModel = new TeamSelectionViewModel(new FakeTeamsGateway(), new FakeDialogs())
        {
            SearchText = "開発"
        };
        Assert.True(viewModel.HasSearchText);
        Assert.True(viewModel.ClearSearchCommand.CanExecute(null));

        viewModel.ClearSearchCommand.Execute(null);

        Assert.Equal("", viewModel.SearchText);
        Assert.False(viewModel.HasSearchText);
    }

    [Fact]
    public void TeamSelection_PrepareSelectionCommandは検索をクリアしてフォーカス要求を通知する()
    {
        var viewModel = new TeamSelectionViewModel(new FakeTeamsGateway(), new FakeDialogs())
        {
            SearchText = "開発"
        };
        var requested = false;
        viewModel.SelectionFocusRequested += () => requested = true;

        viewModel.PrepareSelectionCommand.Execute(null);

        Assert.Equal("", viewModel.SearchText);
        Assert.True(requested);
    }
}
