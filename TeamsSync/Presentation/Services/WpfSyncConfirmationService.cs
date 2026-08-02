using System.Windows;
using System.Windows.Controls;

using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.ViewModels;

using Wpf.Ui;
using Wpf.Ui.Controls;

using TextBlock = System.Windows.Controls.TextBlock;

namespace TeamsSync.Presentation.Services;

/// <summary>同期実行前の最終確認ダイアログをWPF-UIのContentDialogとして表示する。</summary>
public sealed class WpfSyncConfirmationService(
    IContentDialogService contentDialogs) : ISyncConfirmationService
{
    private const int VisibleRemovalTargetCount = 10;
    /// <summary>
    ///     対象チーム・件数内訳・入力元を表示する確認ダイアログを表示する。
    /// </summary>
    public async Task<bool> ConfirmSyncAsync(SyncConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        SyncPlan plan = confirmation.Plan;

        // 固定幅(旧: Width=440)だと高DPIや長いチーム名・長い入力概要で内容が欠けるため、
        // MinWidthのみを指定して折り返しに任せ、縦方向はScrollViewerへ収めて200%表示でも
        // 「同期を実行」ボタンが画面外に押し出されないようにする。
        StackPanel content = new() { MinWidth = 340 };
        content.Children.Add(BuildHeaderBlock(plan));
        if (plan.RemoveCount > 0)
        {
            content.Children.Add(BuildRemovalWarningBox(plan));
        }

        if (plan.RemoveCount > 0)
        {
            content.Children.Add(BuildRemovalTargets(plan));
        }

        content.Children.Add(BuildCountsSection(plan));
        foreach (UIElement child in BuildInputSourceBlocks(confirmation))
        {
            content.Children.Add(child);
        }

        ScrollViewer scrollViewer = new()
        {
            Content = content,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        ContentDialog dialog = new()
        {
            Title = ConfirmationDialogHelper.BuildTitle("同期の最終確認"),
            Content = scrollViewer,
            PrimaryButtonText = "同期を実行",
            PrimaryButtonIcon = new SymbolIcon { Symbol = SymbolRegular.ArrowSync24 },
            CloseButtonText = "キャンセル",
            CloseButtonIcon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24 },
            DefaultButton = ContentDialogButton.Close
        };

        return await ConfirmationDialogHelper.ShowRestoringFocusAsync(contentDialogs, dialog, cancellationToken) ==
               ContentDialogResult.Primary;
    }

    /// <summary>対象チーム名と同期モードを表示するヘッダー部分を組み立てる。</summary>
    private static UIElement BuildHeaderBlock(SyncPlan plan)
    {
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            Text = $"対象チーム: {plan.Team.DisplayName}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"同期モード: {SyncWorkspaceTextFormatter.BuildModeLabel(plan.Mode)}",
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    // 削除は取り消せない操作のため、件数情報の中で最も目立つ位置・書式(枠線+強調色+アイコン)で表示する。
    /// <summary>削除件数を強調表示する警告ボックスを組み立てる。</summary>
    private static Border BuildRemovalWarningBox(SyncPlan plan)
    {
        Border removalBox = new()
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 12, 0, 0)
        };
        removalBox.SetResourceReference(Border.BorderBrushProperty, "SystemFillColorCriticalBrush");
        removalBox.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        StackPanel removalPanel = new() { Orientation = Orientation.Horizontal };
        SymbolIcon removalIcon = new()
        {
            Symbol = SymbolRegular.Warning24,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 8, 0)
        };
        removalIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "SystemFillColorCriticalBrush");
        TextBlock removalText = new()
        {
            Text = plan.Mode == SyncMode.RemoveSpecified
                ? $"削除 {plan.RemoveCount}名 — 入力リストで指定した一般メンバーだけを削除します（所有者は削除されません）"
                : $"削除 {plan.RemoveCount}名 — リストにない一般メンバーを削除します（所有者は削除されません）",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        removalText.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        removalPanel.Children.Add(removalIcon);
        removalPanel.Children.Add(removalText);
        removalBox.Child = removalPanel;
        return removalBox;
    }

    /// <summary>指定削除で実際に削除する対象者を、最終確認ダイアログへ一覧表示する。</summary>
    private static UIElement BuildRemovalTargets(SyncPlan plan)
    {
        List<string> targets = plan.Changes.Where(change => change.Kind == ChangeKind.Remove)
            .Select(change => string.IsNullOrWhiteSpace(change.DisplayName)
                ? change.Email
                : $"{change.DisplayName}（{change.Email}）")
            .ToList();
        IEnumerable<string> visibleTargets = targets.Take(VisibleRemovalTargetCount);
        string remainder = targets.Count > VisibleRemovalTargetCount
            ? $"{Environment.NewLine}ほか {targets.Count - VisibleRemovalTargetCount}名（差分一覧で確認できます）"
            : "";
        return new TextBlock
        {
            Text =
                $"削除対象:{Environment.NewLine}{string.Join(Environment.NewLine, visibleTargets.Select(target => $"・{target}"))}{remainder}",
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
    }

    /// <summary>追加・変更なし・所有者保護の件数を表示する部分を組み立てる。</summary>
    private static UIElement BuildCountsSection(SyncPlan plan)
    {
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            Text = $"追加 {plan.AddCount}名",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        TextBlock secondaryCounts = new()
        {
            Text = $"変更なし {plan.KeepCount}名 ／ 所有者保護 {plan.ProtectedCount}名",
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        secondaryCounts.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(secondaryCounts);
        return panel;
    }

    /// <summary>入力元ファイル名と入力概要を表示するテキスト要素を列挙する。</summary>
    private static IEnumerable<UIElement> BuildInputSourceBlocks(SyncConfirmation confirmation)
    {
        TextBlock inputFileText = new()
        {
            Text = $"入力元: {confirmation.FileName}",
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        inputFileText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        yield return inputFileText;

        TextBlock inputSummaryText = new()
        {
            Text = confirmation.InputSummary, FontSize = 12, TextWrapping = TextWrapping.Wrap
        };
        inputSummaryText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        yield return inputSummaryText;
    }
}
