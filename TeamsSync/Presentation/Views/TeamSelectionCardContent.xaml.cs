using System.Windows.Input;

using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.Views;

/// <summary>チームの検索・選択を行うView</summary>
public partial class TeamSelectionCardContent
{
    private readonly ViewModelEventSubscription<TeamSelectionViewModel> _viewModelSubscription;

    /// <summary>コンストラクター。DataContext変更に合わせてViewModelのイベント購読を切り替える</summary>
    public TeamSelectionCardContent()
    {
        InitializeComponent();
        _viewModelSubscription = new ViewModelEventSubscription<TeamSelectionViewModel>(
            viewModel => viewModel.SelectionFocusRequested += FocusTeamSelection,
            viewModel => viewModel.SelectionFocusRequested -= FocusTeamSelection);
        Loaded += (_, _) => _viewModelSubscription.Load(DataContext as TeamSelectionViewModel);
        Unloaded += (_, _) => _viewModelSubscription.Unload();
        DataContextChanged += (_, e) =>
            _viewModelSubscription.ChangeDataContext(e.NewValue as TeamSelectionViewModel);
    }

    /// <summary>チーム選択コンボボックスへフォーカスし、ドロップダウンを開く</summary>
    private void FocusTeamSelection()
    {
        Dispatcher.BeginInvoke(() =>
        {
            TeamComboBox.Focus();
            Keyboard.Focus(TeamComboBox);
            TeamComboBox.IsDropDownOpen = true;
        });
    }
}