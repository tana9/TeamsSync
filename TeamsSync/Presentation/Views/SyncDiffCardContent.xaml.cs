using System.Windows.Controls;
using System.Windows.Threading;

using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.Views;

/// <summary>同期差分一覧(DataGrid)を表示するView</summary>
public partial class SyncDiffCardContent
{
    private readonly ViewModelEventSubscription<SyncWorkspaceViewModel> _viewModelSubscription;

    /// <summary>コンストラクター。DataContext変更に合わせてViewModelのイベント購読を切り替える</summary>
    public SyncDiffCardContent()
    {
        InitializeComponent();
        _viewModelSubscription = new ViewModelEventSubscription<SyncWorkspaceViewModel>(
            viewModel => viewModel.DiffFocusRequested += FocusSummaryOrFirstError,
            viewModel => viewModel.DiffFocusRequested -= FocusSummaryOrFirstError);
        Loaded += (_, _) => _viewModelSubscription.Load(DataContext as SyncWorkspaceViewModel);
        Unloaded += (_, _) => _viewModelSubscription.Unload();
        DataContextChanged += (_, e) =>
            _viewModelSubscription.ChangeDataContext(e.NewValue as SyncWorkspaceViewModel);
    }

    // 差分確認・再検証・同期後の再取得で一覧が更新されたときに呼ばれる。
    // エラーがあれば最初のエラー行(現在の表示フィルターに含まれる場合)、なければ集計テキストへフォーカスを移す
    /// <summary>差分一覧更新時に、最初のエラー行(あれば)または集計テキストへフォーカスを移す</summary>
    private void FocusSummaryOrFirstError()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is not SyncWorkspaceViewModel viewModel)
            {
                return;
            }

            SyncChangeRowViewModel? firstError =
                viewModel.Plan.Changes.FirstOrDefault(change => change.Kind == ChangeKind.Error);
            if (firstError is not null && ChangesGrid.Items.Contains(firstError))
            {
                ChangesGrid.SelectedItem = firstError;
                ChangesGrid.ScrollIntoView(firstError);
                ChangesGrid.UpdateLayout();
                if (ChangesGrid.ItemContainerGenerator.ContainerFromItem(firstError) is DataGridRow row)
                {
                    row.Focus();
                }
                else
                {
                    ChangesGrid.Focus();
                }

                return;
            }

            SummaryTextBlock.Focus();
        }, DispatcherPriority.Background);
    }
}
