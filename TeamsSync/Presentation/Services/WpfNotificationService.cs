using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

using Microsoft.Extensions.Logging;

using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace TeamsSync.Presentation.Services;

/// <summary>スナックバー・ダイアログを用いて成功・警告・エラーをユーザーへ通知する</summary>
public sealed class WpfNotificationService(
    ISnackbarService snackbars,
    IContentDialogService contentDialogs,
    ILogger<WpfNotificationService> logger) : INotificationService
{
    private static readonly TimeSpan PersistentErrorTimeout = TimeSpan.FromDays(1);

    /// <inheritdoc />
    public void ShowSuccess(string title, string message)
    {
        snackbars.Show(title, message, ControlAppearance.Success, TimeSpan.FromSeconds(5));
    }

    /// <inheritdoc />
    public void ShowWarning(string title, string message)
    {
        snackbars.Show(title, message, ControlAppearance.Caution, TimeSpan.FromSeconds(8));
    }

    /// <inheritdoc />
    public void ShowSuccessWithAction(string title, string message, string actionText, Action action)
    {
        ShowWithAction(title, message, actionText, action, ControlAppearance.Success, TimeSpan.FromSeconds(8),
            SymbolRegular.DocumentCsv24);
    }

    /// <inheritdoc />
    public void ShowWarningWithAction(string title, string message, string actionText, Action action)
    {
        ShowWithAction(title, message, actionText, action, ControlAppearance.Caution, TimeSpan.FromSeconds(10),
            SymbolRegular.DocumentCsv24);
    }

    /// <inheritdoc />
    public Task ShowErrorAsync(string message, string title = "エラー", Action? onClosed = null)
    {
        ShowWithAction(title, message, "詳細をコピー", () => Clipboard.SetText(message),
            ControlAppearance.Danger, PersistentErrorTimeout, SymbolRegular.Copy24, true);
        InvokeCallbackSafely(onClosed, title, logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowCriticalErrorAsync(string message, string title = "エラー", Action? onClosed = null)
    {
        return ShowErrorSafelyAsync(async () =>
        {
            TextBox textBox = new()
            {
                Text = message,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                MaxHeight = 240,
                MinWidth = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            textBox.Loaded += (_, _) => textBox.SelectAll();
            UIElement dialogTitle = ConfirmationDialogHelper.BuildTitle(title, SymbolRegular.ErrorCircle24,
                "SystemFillColorCriticalBrush");
            await ConfirmationDialogHelper.ShowRestoringFocusAsync(contentDialogs,
                new ContentDialog { Title = dialogTitle, Content = textBox, CloseButtonText = "閉じる" },
                CancellationToken.None);
        }, title, onClosed, logger);
    }

    private void ShowWithAction(string title, string message, string actionText, Action action,
        ControlAppearance appearance, TimeSpan timeout, SymbolRegular actionIcon, bool enableCloseButton = false)
    {
        snackbars.Show(title, message, appearance, timeout);
        Snackbar? snackbar = snackbars.GetSnackbarPresenter()?.Content;
        if (snackbar is null)
        {
            return;
        }

        snackbar.IsCloseButtonEnabled = enableCloseButton;
        snackbar.Content = BuildActionContent(message, actionText, actionIcon,
            () => ExecuteAction(action, title));
    }

    private static Grid BuildActionContent(string message, string actionText, SymbolRegular actionIcon,
        Action executeAction)
    {
        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock messageText = new()
        {
            Text = message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(messageText);
        TextBlock actionTextBlock = BuildActionLink(actionText, actionIcon, executeAction);
        Grid.SetColumn(actionTextBlock, 1);
        content.Children.Add(actionTextBlock);
        return content;
    }

    private static TextBlock BuildActionLink(string actionText, SymbolRegular actionIcon, Action executeAction)
    {
        TextBlock actionTextBlock = new()
        {
            Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        // 危険/成功/注意いずれの塗りつぶし背景上でも視認できるよう、Snackbar既定のリンク色に
        // 任せず「アクセント色背景上のテキスト」用ブラシを明示する。あわせてSemiBoldにして、
        // 隣接する閉じるボタンと並んだときにクリック可能な要素だと分かるようにする
        Hyperlink actionLink = new() { FontWeight = FontWeights.SemiBold };
        actionLink.SetResourceReference(TextElement.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        AutomationProperties.SetName(actionLink, actionText);
        SymbolIcon icon = new() { Symbol = actionIcon, FontSize = 16, Margin = new Thickness(0, 0, 4, 0) };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        actionLink.Inlines.Add(new InlineUIContainer(icon));
        actionLink.Inlines.Add(new Run(actionText));
        actionLink.Click += (_, _) => executeAction();
        actionTextBlock.Inlines.Add(actionLink);
        return actionTextBlock;
    }

    private void ExecuteAction(Action action, string title)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Snackbarのアクション実行に失敗しました。Title={Title}", title);
            ShowWarning("操作を完了できませんでした", ex.Message);
        }
    }

    internal static void InvokeCallbackSafely(Action? callback, string title, ILogger logger)
    {
        try { callback?.Invoke(); }
        catch (Exception ex) { logger.LogError(ex, "エラー通知後の処理に失敗しました。Title={Title}", title); }
    }

    internal static async Task ShowErrorSafelyAsync(Func<Task> showDialog, string title,
        Action? onClosed, ILogger logger)
    {
        try { await showDialog(); }
        catch (Exception ex) { logger.LogError(ex, "エラーダイアログを表示できませんでした。Title={Title}", title); }
        finally
        {
            try { onClosed?.Invoke(); }
            catch (Exception ex) { logger.LogError(ex, "エラーダイアログ終了後の処理に失敗しました。Title={Title}", title); }
        }
    }
}
