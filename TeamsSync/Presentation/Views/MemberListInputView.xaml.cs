using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.Views;

/// <summary>メンバーリストの入力(ファイル選択・ドロップ・テキスト貼り付け)を行うView。</summary>
public partial class MemberListInputView
{
    private readonly ViewModelEventSubscription<MemberFileViewModel> _viewModelSubscription;

    /// <summary>コンストラクター。DataContext変更に合わせてViewModelのイベント購読を切り替える。</summary>
    public MemberListInputView()
    {
        InitializeComponent();
        _viewModelSubscription = new ViewModelEventSubscription<MemberFileViewModel>(
            viewModel => viewModel.InputFocusRequested += FocusInputTarget,
            viewModel => viewModel.InputFocusRequested -= FocusInputTarget);
        Loaded += (_, _) => _viewModelSubscription.Load(DataContext as MemberFileViewModel);
        Unloaded += (_, _) => _viewModelSubscription.Unload();
        DataContextChanged += (_, e) =>
            _viewModelSubscription.ChangeDataContext(e.NewValue as MemberFileViewModel);
    }

    // ファイル読込または貼り付け解析が失敗したときに呼ばれる。選択中のタブに応じて、
    // 修正操作の起点(ファイル選択ボタン、または貼り付けテキスト欄)へフォーカスを移す。
    private void FocusInputTarget()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is not MemberFileViewModel viewModel)
            {
                return;
            }

            if (viewModel.SelectedInputIndex == 1)
            {
                PasteTextBox.Focus();
            }
            else
            {
                BrowseButton.Focus();
            }
        });
    }
}