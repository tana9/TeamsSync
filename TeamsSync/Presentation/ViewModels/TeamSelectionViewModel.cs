using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TeamsSync.Application.Abstractions;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

public partial class TeamSelectionViewModel : ObservableObject
{
    private readonly BusyOperationRunner _busyRunner;
    private readonly INotificationService _dialogs;
    private readonly ITeamsGateway _teamsGateway;
    private string? _currentUserId;
    private bool _externallyBusy;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string searchText = "";

    [ObservableProperty] private TeamInfo? selectedTeam;

    public TeamSelectionViewModel(ITeamsGateway teamsGateway, INotificationService dialogs)
    {
        _teamsGateway = teamsGateway;
        _dialogs = dialogs;
        _busyRunner = new BusyOperationRunner(_dialogs, (message, isError) => StatusChanged?.Invoke(message, isError));
        TeamsView = CollectionViewSource.GetDefaultView(Teams);
        TeamsView.Filter = item => item is TeamInfo team && MatchesSearch(team);
    }

    public ObservableCollection<TeamInfo> Teams { get; } = [];
    public ICollectionView TeamsView { get; }
    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public bool HasNoSearchResults =>
        !string.IsNullOrWhiteSpace(SearchText) && Teams.Count > 0 && !Teams.Any(MatchesSearch);

    public event Action? SelectionChanged;
    public event Action? SelectionFocusRequested;
    public event Action<bool>? ActivityChanged;
    public event Action<string, bool>? StatusChanged;

    public async Task InitializeAsync(string currentUserId, CancellationToken cancellationToken = default)
    {
        _currentUserId = currentUserId;
        await LoadAsync(cancellationToken);
    }

    public void Clear()
    {
        _teamsGateway.ClearOwnedTeamsCache(_currentUserId);
        _currentUserId = null;
        Teams.Clear();
        OnPropertyChanged(nameof(HasNoSearchResults));
        SelectedTeam = null;
    }

    public void SetExternalBusy(bool value)
    {
        _externallyBusy = value;
        RefreshCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTeamChanged(TeamInfo? value)
    {
        SelectionChanged?.Invoke();
    }

    partial void OnSearchTextChanged(string value)
    {
        TeamsView.Refresh();
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(HasNoSearchResults));
        ClearSearchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    private void ClearSearch()
    {
        SearchText = "";
    }

    [RelayCommand]
    private void PrepareSelection()
    {
        SearchText = "";
        SelectionFocusRequested?.Invoke();
    }

    private bool CanClearSearch()
    {
        return HasSearchText;
    }

    private bool MatchesSearch(TeamInfo team)
    {
        return string.IsNullOrWhiteSpace(SearchText) ||
               team.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        _teamsGateway.ClearOwnedTeamsCache(_currentUserId);
        await _busyRunner.RunAsync(async () =>
        {
            await LoadAsync();
            _dialogs.ShowSuccess("チーム一覧を更新しました", Teams.Count == 0
                ? "所有しているチームが見つかりません"
                : $"{Teams.Count}件のチームが見つかりました");
        });
    }

    private bool CanRefresh()
    {
        return _currentUserId is not null && !IsBusy && !_externallyBusy;
    }

    private async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUserId is null) return;
        IsBusy = true;
        ActivityChanged?.Invoke(true);
        RefreshCommand.NotifyCanExecuteChanged();
        try
        {
            StatusChanged?.Invoke("所有しているチームを検索しています…", false);
            var owned = await _teamsGateway.GetOwnedTeamsAsync(_currentUserId, cancellationToken);
            Teams.Clear();
            foreach (var team in owned) Teams.Add(team);
            SelectedTeam = null;
            StatusChanged?.Invoke(Teams.Count == 0
                ? "所有しているチームが見つかりません"
                : $"{Teams.Count}件のチームが見つかりました", false);
            OnPropertyChanged(nameof(HasNoSearchResults));
        }
        finally
        {
            IsBusy = false;
            ActivityChanged?.Invoke(false);
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }
}
