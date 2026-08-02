using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

using Wpf.Ui;
using Wpf.Ui.Controls;

using TextBlock = System.Windows.Controls.TextBlock;

namespace TeamsSync.Presentation.Services;

/// <summary>
///     確認系ContentDialog(同期最終確認・入力置き換え確認)が共通で使う
///     タイトル組み立てとフォーカス復元処理を提供する
/// </summary>
internal static class ConfirmationDialogHelper
{
    // タイトルだけでは一見して確認ダイアログと見分けがつかないため、エラーダイアログ(WpfNotificationService.ShowError)
    // と同様にアイコンを添えて、ひと目で「実行前の確認」だと分かるようにする。AutomationProperties.Nameは
    // Titleオブジェクト全体に付け、読み上げがアイコン分だけ冗長にならないようにする
    /// <summary>アイコン付きの確認ダイアログタイトルを組み立てる</summary>
    public static UIElement BuildTitle(string titleText)
    {
        StackPanel titlePanel = new() { Orientation = Orientation.Horizontal };
        AutomationProperties.SetName(titlePanel, titleText);
        SymbolIcon titleIcon = new()
        {
            Symbol = SymbolRegular.QuestionCircle24,
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        TextBlock titleTextBlock = new()
        {
            Text = titleText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(titleIcon);
        titlePanel.Children.Add(titleTextBlock);
        return titlePanel;
    }

    // ContentDialogHostはウィンドウ内オーバーレイであり、通常のモーダルウィンドウと違って
    // 閉じた後にフォーカスが自動復帰しない。呼び出し元のボタンへ確実に戻すため、表示前の
    // フォーカス要素を記録しておき、閉じた後に明示的に戻す
    /// <summary>
    ///     ダイアログを表示し、閉じた後に表示前フォーカスしていた要素へフォーカスを明示的に戻す
    /// </summary>
    public static async Task<ContentDialogResult> ShowRestoringFocusAsync(
        IContentDialogService contentDialogs, ContentDialog dialog, CancellationToken cancellationToken)
    {
        IInputElement? previouslyFocused = Keyboard.FocusedElement;
        try
        {
            return await contentDialogs.ShowAsync(dialog, cancellationToken);
        }
        finally
        {
            if (previouslyFocused is not null)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Keyboard.Focus(previouslyFocused));
            }
        }
    }
}