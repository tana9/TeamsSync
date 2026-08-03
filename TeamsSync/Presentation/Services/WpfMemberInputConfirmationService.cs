using System.Windows;

using Wpf.Ui;
using Wpf.Ui.Controls;

using TextBlock = System.Windows.Controls.TextBlock;

namespace TeamsSync.Presentation.Services;

/// <summary>既存のメンバー入力を置き換える前の確認ダイアログをWPF-UIのContentDialogとして表示する</summary>
public sealed class WpfMemberInputConfirmationService(
    IContentDialogService contentDialogs) : IMemberInputConfirmationService
{
    /// <inheritdoc />
    public async Task<bool> ConfirmReplaceMemberInputAsync(string teamName, int memberCount,
        CancellationToken cancellationToken = default)
    {
        ContentDialog dialog = ConfirmationDialogHelper.BuildConfirmDialog("現在の入力を置き換えますか？",
            new TextBlock
            {
                Text = $"現在のファイルまたはテキスト入力を、{teamName}の一般メンバー{memberCount}名で置き換えます。",
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 320
            }, "置き換える", SymbolRegular.Checkmark24);
        return await ConfirmationDialogHelper.ShowRestoringFocusAsync(contentDialogs, dialog, cancellationToken) ==
               ContentDialogResult.Primary;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmReplaceTextWithFileContentAsync(string fileName, int memberCount,
        CancellationToken cancellationToken = default)
    {
        ContentDialog dialog = ConfirmationDialogHelper.BuildConfirmDialog("テキスト入力を置き換えますか？",
            new TextBlock
            {
                Text = $"現在のテキスト入力を、{fileName}から読み取った{memberCount}件で置き換えます。元のファイルは変更されません。",
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 320
            }, "置き換える", SymbolRegular.Checkmark24);
        return await ConfirmationDialogHelper.ShowRestoringFocusAsync(contentDialogs, dialog, cancellationToken) ==
               ContentDialogResult.Primary;
    }
}