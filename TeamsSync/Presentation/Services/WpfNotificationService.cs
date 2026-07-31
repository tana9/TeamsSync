using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace TeamsSync.Presentation.Services;

/// <summary>スナックバー・ダイアログを用いて成功・警告・エラーをユーザーへ通知する。</summary>
public sealed class WpfNotificationService(
    ISnackbarService snackbars,
    IContentDialogService contentDialogs,
    ILogger<WpfNotificationService> logger) : INotificationService
{
    public void ShowSuccess(string title, string message) =>
        snackbars.Show(title, message, ControlAppearance.Success, TimeSpan.FromSeconds(5));

    public void ShowWarning(string title, string message) =>
        snackbars.Show(title, message, ControlAppearance.Caution, TimeSpan.FromSeconds(8));

    public Task ShowErrorAsync(string message, string title = "エラー", Action? onClosed = null)
    {
        return ShowErrorSafelyAsync(async () =>
        {
            var textBox = new TextBox
            {
                Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true, MaxHeight = 240, MinWidth = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            textBox.Loaded += (_, _) => textBox.SelectAll();
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            AutomationProperties.SetName(titlePanel, title);
            var titleIcon = new SymbolIcon
            {
                Symbol = SymbolRegular.ErrorCircle24, FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            titleIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "SystemFillColorCriticalBrush");
            titlePanel.Children.Add(titleIcon);
            titlePanel.Children.Add(new TextBlock
            {
                Text = title, FontWeight = FontWeights.SemiBold, FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center
            });
            await WpfSyncConfirmationService.ShowRestoringFocusAsync(contentDialogs, new ContentDialog
            {
                Title = titlePanel, Content = textBox, CloseButtonText = "閉じる"
            }, CancellationToken.None);
        }, title, onClosed, logger);
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
