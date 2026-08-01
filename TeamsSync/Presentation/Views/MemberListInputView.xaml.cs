using System.Windows;

using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.Views;

/// <summary>メンバーリストの入力(ファイル選択・ドロップ・テキスト貼り付け)を行うView。</summary>
public partial class MemberListInputView
{
    /// <summary>コンストラクター。DataContext変更に合わせてViewModelのイベント購読を切り替える。</summary>
    public MemberListInputView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Subscribe(DataContext as MemberFileViewModel, false);
    }

    /// <summary>DataContextの変更に合わせて、旧ViewModelの購読を解除し新ViewModelを購読する。</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Subscribe(e.OldValue as MemberFileViewModel, false);
        Subscribe(e.NewValue as MemberFileViewModel, true);
    }

    /// <summary>ViewModelの<see cref="MemberFileViewModel.InputFocusRequested" />イベントを購読/解除する。</summary>
    private void Subscribe(MemberFileViewModel? viewModel, bool subscribe)
    {
        if (viewModel is null)
        {
            return;
        }

        if (subscribe)
        {
            viewModel.InputFocusRequested += FocusInputTarget;
        }
        else
        {
            viewModel.InputFocusRequested -= FocusInputTarget;
        }
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