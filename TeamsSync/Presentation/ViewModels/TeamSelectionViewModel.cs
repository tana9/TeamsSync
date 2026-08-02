using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     サインイン中ユーザーが所有するチームの一覧取得・検索・選択を管理する
/// </summary>
public partial class TeamSelectionViewModel : ObservableObject
{
    private readonly BusyOperationRunner _busyRunner;
    private readonly INotificationService _dialogs;
    private readonly TeamsAccessService _teamsAccess;
    private string? _currentUserId;
    private bool _externallyBusy;

    /// <summary>コンストラクター。検索用のコレクションビューを初期化する</summary>
    public TeamSelectionViewModel(TeamsAccessService teamsAccess, INotificationService dialogs)
    {
        _teamsAccess = teamsAccess;
        _dialogs = dialogs;
        _busyRunner = new BusyOperationRunner(_dialogs, (message, isError) => StatusChanged?.Invoke(message, isError),
            value => IsBusy = value);
        TeamsView = CollectionViewSource.GetDefaultView(Teams);
        TeamsView.Filter = item => item is TeamInfo team && MatchesSearch(team);
    }

    /// <summary>チーム一覧の取得中かどうか</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>チーム検索欄の入力テキスト</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    /// <summary>選択中のチーム</summary>
    [ObservableProperty]
    public partial TeamInfo? SelectedTeam { get; set; }

    /// <summary>所有チームの一覧</summary>
    public ObservableCollection<TeamInfo> Teams { get; } = [];

    /// <summary>検索テキストによるフィルターを適用した<see cref="Teams" />のビュー</summary>
    public ICollectionView TeamsView { get; }

    /// <summary>検索テキストが入力されているかどうか</summary>
    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    /// <summary>チームの検索・選択を操作できるかどうか</summary>
    public bool IsSelectionEnabled => !IsBusy && !_externallyBusy;

    /// <summary>検索条件に一致するチームが1件もないかどうか</summary>
    public bool HasNoSearchResults =>
        !string.IsNullOrWhiteSpace(SearchText) && Teams.Count > 0 && !Teams.Any(MatchesSearch);

    /// <summary>チーム選択欄へフォーカスを移すよう要求するために発行される</summary>
    public event Action? SelectionFocusRequested;

    /// <summary>ステータスメッセージを通知するために発行される</summary>
    public event Action<string, bool>? StatusChanged;

    /// <summary>ユーザーIDを設定し、所有チーム一覧を初期取得する</summary>
    public async Task InitializeAsync(string currentUserId, CancellationToken cancellationToken = default)
    {
        _currentUserId = currentUserId;
        await LoadAsync(cancellationToken: cancellationToken);
    }

    /// <summary>サインアウト時などに、選択状態・チーム一覧・キャッシュをクリアする</summary>
    public void Clear()
    {
        _teamsAccess.ClearOwnedTeamsCache(_currentUserId);
        _currentUserId = null;
        Teams.Clear();
        OnPropertyChanged(nameof(HasNoSearchResults));
        SelectedTeam = null;
    }

    /// <summary>他画面の処理中状態を反映し、更新コマンドの実行可否を再評価する</summary>
    public void SetExternalBusy(bool value)
    {
        if (_externallyBusy == value)
        {
            return;
        }

        _externallyBusy = value;
        OnPropertyChanged(nameof(IsSelectionEnabled));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSelectionEnabled));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    /// <summary>検索テキストの変更に応じてビューを再フィルターし、関連プロパティを更新する</summary>
    partial void OnSearchTextChanged(string value)
    {
        TeamsView.Refresh();
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(HasNoSearchResults));
        ClearSearchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>検索テキストをクリアする</summary>
    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    private void ClearSearch()
    {
        SearchText = "";
    }

    /// <summary>検索テキストをクリアし、チーム選択欄へのフォーカスを要求する</summary>
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

    /// <summary>チームが現在の検索テキストに一致するかどうかを判定する</summary>
    private bool MatchesSearch(TeamInfo team)
    {
        return string.IsNullOrWhiteSpace(SearchText) ||
               team.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>所有チーム判定のキャッシュをクリアしたうえで一覧を再取得する</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (await LoadAsync(true))
        {
            _dialogs.ShowSuccess("チーム一覧を更新しました", Teams.Count == 0
                ? "所有しているチームが見つかりません"
                : $"{Teams.Count}件のチームが見つかりました");
        }
    }

    private bool CanRefresh()
    {
        return _currentUserId is not null && !IsBusy && !_externallyBusy;
    }

    /// <summary>
    ///     所有チーム一覧をGraph APIから取得し、<see cref="Teams" />へ反映する。選択中のチームが
    ///     再取得後も引き続き所有チームに含まれていれば、選択状態を維持する
    /// </summary>
    private async Task<bool> LoadAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (_currentUserId is null)
        {
            return false;
        }

        string? previouslySelectedTeamId = SelectedTeam?.Id;
        return await _busyRunner.RunAsync(async () =>
        {
            StatusChanged?.Invoke("所有しているチームを検索しています…", false);
            IReadOnlyList<TeamInfo> owned = await _teamsAccess.GetOwnedTeamsAsync(
                _currentUserId, refresh, cancellationToken);
            Teams.Clear();
            foreach (TeamInfo team in owned)
            {
                Teams.Add(team);
            }

            SelectedTeam = previouslySelectedTeamId is not null
                ? Teams.FirstOrDefault(team => team.Id == previouslySelectedTeamId)
                : null;
            StatusChanged?.Invoke(Teams.Count == 0
                ? "所有しているチームが見つかりません"
                : $"{Teams.Count}件のチームが見つかりました", false);
            OnPropertyChanged(nameof(HasNoSearchResults));
        }, cancellationToken: cancellationToken);
    }
}
