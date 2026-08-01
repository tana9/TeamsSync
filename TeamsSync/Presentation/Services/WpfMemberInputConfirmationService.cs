using System.Windows;

using Wpf.Ui;
using Wpf.Ui.Controls;

using TextBlock = System.Windows.Controls.TextBlock;

namespace TeamsSync.Presentation.Services;

/// <summary>既存のメンバー入力を置き換える前の確認ダイアログをWPF-UIのContentDialogとして表示する。</summary>
public sealed class WpfMemberInputConfirmationService(
    IContentDialogService contentDialogs) : IMemberInputConfirmationService
{
    /// <inheritdoc />
    public async Task<bool> ConfirmReplaceMemberInputAsync(string teamName, int memberCount,
        CancellationToken cancellationToken = default)
    {
        ContentDialog dialog = new()
        {
            Title = ConfirmationDialogHelper.BuildTitle("現在の入力を置き換えますか？"),
            Content = new TextBlock
            {
                Text = $"現在のファイルまたはテキスト入力を、{teamName}の一般メンバー{memberCount}名で置き換えます。",
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 320
            },
            PrimaryButtonText = "置き換える",
            PrimaryButtonIcon = new SymbolIcon { Symbol = SymbolRegular.Checkmark24 },
            CloseButtonText = "キャンセル",
            CloseButtonIcon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 },
            DefaultButton = ContentDialogButton.Close
        };
        return await ConfirmationDialogHelper.ShowRestoringFocusAsync(contentDialogs, dialog, cancellationToken) ==
               ContentDialogResult.Primary;
    }
}