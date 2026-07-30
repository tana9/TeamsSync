using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.Views;

public partial class SyncDiffCardContent
{
    public SyncDiffCardContent()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Subscribe(DataContext as SyncWorkspaceViewModel, false);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Subscribe(e.OldValue as SyncWorkspaceViewModel, false);
        Subscribe(e.NewValue as SyncWorkspaceViewModel, true);
    }

    private void Subscribe(SyncWorkspaceViewModel? viewModel, bool subscribe)
    {
        if (viewModel is null) return;
        if (subscribe)
            viewModel.DiffFocusRequested += FocusSummaryOrFirstError;
        else
            viewModel.DiffFocusRequested -= FocusSummaryOrFirstError;
    }

    // 差分確認・再検証・同期後の再取得で一覧が更新されたときに呼ばれる。
    // エラーがあれば最初のエラー行(現在の表示フィルターに含まれる場合)、なければ集計テキストへフォーカスを移す。
    private void FocusSummaryOrFirstError()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is not SyncWorkspaceViewModel viewModel) return;

            var firstError = viewModel.Changes.FirstOrDefault(change => change.Kind == ChangeKind.Error);
            if (firstError is not null && ChangesGrid.Items.Contains(firstError))
            {
                ChangesGrid.SelectedItem = firstError;
                ChangesGrid.ScrollIntoView(firstError);
                ChangesGrid.UpdateLayout();
                if (ChangesGrid.ItemContainerGenerator.ContainerFromItem(firstError) is DataGridRow row)
                    row.Focus();
                else
                    ChangesGrid.Focus();
                return;
            }

            SummaryTextBlock.Focus();
        }, DispatcherPriority.Background);
    }
}
