using System.Windows;
using System.Windows.Input;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.Views;

public partial class TeamSelectionCardContent
{
    public TeamSelectionCardContent()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Subscribe(DataContext as TeamSelectionViewModel, false);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Subscribe(e.OldValue as TeamSelectionViewModel, false);
        Subscribe(e.NewValue as TeamSelectionViewModel, true);
    }

    private void Subscribe(TeamSelectionViewModel? viewModel, bool subscribe)
    {
        if (viewModel is null) return;
        if (subscribe)
            viewModel.SelectionFocusRequested += FocusTeamSelection;
        else
            viewModel.SelectionFocusRequested -= FocusTeamSelection;
    }

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
