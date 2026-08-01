using System.Windows;
using System.Windows.Input;

using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.Views;

/// <summary>チームの検索・選択を行うView。</summary>
public partial class TeamSelectionCardContent
{
    /// <summary>コンストラクター。DataContext変更に合わせてViewModelのイベント購読を切り替える。</summary>
    public TeamSelectionCardContent()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Subscribe(DataContext as TeamSelectionViewModel, false);
    }

    /// <summary>DataContextの変更に合わせて、旧ViewModelの購読を解除し新ViewModelを購読する。</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Subscribe(e.OldValue as TeamSelectionViewModel, false);
        Subscribe(e.NewValue as TeamSelectionViewModel, true);
    }

    /// <summary>ViewModelの<see cref="TeamSelectionViewModel.SelectionFocusRequested" />イベントを購読/解除する。</summary>
    private void Subscribe(TeamSelectionViewModel? viewModel, bool subscribe)
    {
        if (viewModel is null)
        {
            return;
        }

        if (subscribe)
        {
            viewModel.SelectionFocusRequested += FocusTeamSelection;
        }
        else
        {
            viewModel.SelectionFocusRequested -= FocusTeamSelection;
        }
    }

    /// <summary>チーム選択コンボボックスへフォーカスし、ドロップダウンを開く。</summary>
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