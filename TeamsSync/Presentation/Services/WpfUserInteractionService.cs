using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TeamsSync.Domain.Teams;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace TeamsSync.Presentation.Services;

/// <summary>WPF-UIのダイアログ・スナックバー表示先ホストをメインウィンドウへ結び付ける。</summary>
public sealed class WpfUserInteractionHost(
    IContentDialogService contentDialogs,
    ISnackbarService snackbars) : IUserInteractionHost
{
    /// <summary>ダイアログホストとスナックバー表示先を登録する。</summary>
    public void SetHosts(ContentDialogHost dialogHost, SnackbarPresenter snackbarPresenter)
    {
        contentDialogs.SetDialogHost(dialogHost);
        snackbars.SetSnackbarPresenter(snackbarPresenter);
    }
}

/// <summary>Win32の標準ファイルダイアログでメンバーリスト・同期結果ファイルを選択させる。</summary>
public sealed class WpfFilePickerService : IFilePickerService
{
    /// <summary>メンバーリストファイル(CSV/Excel)を選択するダイアログを表示する。</summary>
    public string? PickMemberFile(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "メンバーリストを選択",
            Filter = "メンバーリスト (*.csv;*.xlsx)|*.csv;*.xlsx|CSV (*.csv)|*.csv|Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>同期結果の保存先ファイルを選択するダイアログを表示する。既定のファイル名にチーム名・日時を含める。</summary>
    public string? PickResultFile(string? initialDirectory, string teamName)
    {
        var safeName = string.Concat(teamName.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog
        {
            Title = "同期結果を保存",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"{safeName}_sync_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

/// <summary>同期実行前の最終確認ダイアログをWPF-UIのContentDialogとして表示する。</summary>
public sealed class WpfSyncConfirmationService(
    IContentDialogService contentDialogs) : ISyncConfirmationService
{
    /// <summary>
    /// 対象チーム・件数内訳・入力元を表示する確認ダイアログを表示する。削除がある場合は
    /// 確認チェックボックスへの同意があるまで実行ボタンを無効化する。
    /// </summary>
    public async Task<bool> ConfirmSyncAsync(SyncConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        var plan = confirmation.Plan;

        // 固定幅(旧: Width=440)だと高DPIや長いチーム名・長い入力概要で内容が欠けるため、
        // MinWidthのみを指定して折り返しに任せ、縦方向はScrollViewerへ収めて200%表示でも
        // 「同期を実行」ボタンが画面外に押し出されないようにする。
        var content = new StackPanel { MinWidth = 340 };
        content.Children.Add(BuildHeaderBlock(plan));
        if (plan.RemoveCount > 0) content.Children.Add(BuildRemovalWarningBox(plan));
        content.Children.Add(BuildCountsSection(plan));
        foreach (var child in BuildInputSourceBlocks(confirmation)) content.Children.Add(child);

        var acknowledgement = plan.RemoveCount > 0 ? BuildAcknowledgementCheckbox(plan) : null;
        if (acknowledgement is not null) content.Children.Add(acknowledgement);

        var scrollViewer = new ScrollViewer
        {
            Content = content,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var dialog = new ContentDialog
        {
            Title = plan.IsLargeRemoval ? "大量削除の確認" : "同期の最終確認",
            Content = scrollViewer,
            PrimaryButtonText = "同期を実行",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = acknowledgement is null
        };
        if (acknowledgement is not null)
        {
            acknowledgement.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            acknowledgement.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        }

        return await ShowRestoringFocusAsync(contentDialogs, dialog, cancellationToken) == ContentDialogResult.Primary;
    }

    /// <summary>対象チーム名と同期モードを表示するヘッダー部分を組み立てる。</summary>
    private static UIElement BuildHeaderBlock(SyncPlan plan)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"対象チーム: {plan.Team.DisplayName}",
            FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text =
                $"同期モード: {(plan.Mode == SyncMode.AddOnly ? "追加のみ（既存メンバーを維持）" : "完全同期（リスト外を削除）")}",
            Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    // 削除は取り消せない操作のため、件数情報の中で最も目立つ位置・書式(枠線+強調色+アイコン)で表示する。
    /// <summary>削除件数を強調表示する警告ボックスを組み立てる。</summary>
    private static Border BuildRemovalWarningBox(SyncPlan plan)
    {
        var removalBox = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 12, 0, 0)
        };
        removalBox.SetResourceReference(Border.BorderBrushProperty, "SystemFillColorCriticalBrush");
        removalBox.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        var removalPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var removalIcon = new SymbolIcon { Symbol = SymbolRegular.Warning24, FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
        removalIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "SystemFillColorCriticalBrush");
        var removalText = new TextBlock
        {
            Text = $"削除 {plan.RemoveCount}名 — リストにない一般メンバーを削除します（所有者は削除されません）",
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
        };
        removalText.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        removalPanel.Children.Add(removalIcon);
        removalPanel.Children.Add(removalText);
        removalBox.Child = removalPanel;
        return removalBox;
    }

    /// <summary>追加・変更なし・所有者保護の件数を表示する部分を組み立てる。</summary>
    private static UIElement BuildCountsSection(SyncPlan plan)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"追加 {plan.AddCount}名",
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        var secondaryCounts = new TextBlock
        {
            Text = $"変更なし {plan.KeepCount}名 ／ 所有者保護 {plan.ProtectedCount}名",
            Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        secondaryCounts.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        panel.Children.Add(secondaryCounts);
        return panel;
    }

    /// <summary>入力元ファイル名と入力概要を表示するテキスト要素を列挙する。</summary>
    private static IEnumerable<UIElement> BuildInputSourceBlocks(SyncConfirmation confirmation)
    {
        var inputFileText = new TextBlock
        {
            Text = $"入力元: {confirmation.FileName}",
            FontSize = 12, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        inputFileText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        yield return inputFileText;

        var inputSummaryText = new TextBlock
        {
            Text = confirmation.InputSummary, FontSize = 12, TextWrapping = TextWrapping.Wrap
        };
        inputSummaryText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        yield return inputSummaryText;
    }

    /// <summary>削除対象を確認したことを同意させるチェックボックスを組み立てる。</summary>
    private static CheckBox BuildAcknowledgementCheckbox(SyncPlan plan)
    {
        return new CheckBox
        {
            Content = plan.IsLargeRemoval ? "大量削除の対象を確認しました" : "削除対象を確認しました",
            Margin = new Thickness(0, 16, 0, 0)
        };
    }

    // ContentDialogHostはウィンドウ内オーバーレイであり、通常のモーダルウィンドウと違って
    // 閉じた後にフォーカスが自動復帰しない。呼び出し元のボタンへ確実に戻すため、表示前の
    // フォーカス要素を記録しておき、閉じた後に明示的に戻す。
    /// <summary>
    /// ダイアログを表示し、閉じた後に表示前フォーカスしていた要素へフォーカスを明示的に戻す。
    /// </summary>
    internal static async Task<ContentDialogResult> ShowRestoringFocusAsync(
        IContentDialogService contentDialogs, ContentDialog dialog, CancellationToken cancellationToken)
    {
        var previouslyFocused = Keyboard.FocusedElement as IInputElement;
        try
        {
            return await contentDialogs.ShowAsync(dialog, cancellationToken);
        }
        finally
        {
            if (previouslyFocused is not null)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Keyboard.Focus(previouslyFocused));
        }
    }
}

/// <summary>埋め込みリソースのマニュアルHTMLを一時フォルダーへ展開し、既定のブラウザーで開く。</summary>
public sealed class WpfManualService : IManualService
{
    /// <summary>マニュアルHTMLを一時フォルダーへ展開し、既定のブラウザーで開く。</summary>
    public void OpenManual()
    {
        var path = Path.Combine(Path.GetTempPath(), "TeamsSync", "Manual.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var resource = typeof(WpfManualService).Assembly.GetManifestResourceStream("Manual.html")
                               ?? throw new InvalidOperationException("埋め込みマニュアル Manual.html を読み込めません。"))
        using (var file = File.Create(path))
        {
            resource.CopyTo(file);
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}

/// <summary>スナックバー・ダイアログを用いて成功・警告・エラーをユーザーへ通知する。</summary>
public sealed class WpfNotificationService(
    ISnackbarService snackbars,
    IContentDialogService contentDialogs) : INotificationService
{
    /// <summary>成功通知をスナックバーで5秒間表示する。</summary>
    public void ShowSuccess(string title, string message)
    {
        snackbars.Show(title, message, ControlAppearance.Success, TimeSpan.FromSeconds(5));
    }

    /// <summary>警告通知をスナックバーで8秒間表示する。</summary>
    public void ShowWarning(string title, string message)
    {
        snackbars.Show(title, message, ControlAppearance.Caution, TimeSpan.FromSeconds(8));
    }

    /// <summary>エラー内容を選択・コピー可能なテキストボックス付きダイアログで表示する。</summary>
    public async void ShowError(string message, string title = "エラー", Action? onClosed = null)
    {
        try
        {
            var textBox = new TextBox
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

            // タイトルだけでは一見して警告・確認ダイアログと見分けがつかないため、
            // 赤いエラーアイコンを添えてひと目でエラーだと分かるようにする。
            // AutomationProperties.NameはTitleオブジェクト全体に付け、読み上げがアイコン分だけ
            // 冗長にならないようにする。
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            AutomationProperties.SetName(titlePanel, title);
            var titleIcon = new SymbolIcon
            {
                Symbol = SymbolRegular.ErrorCircle24, FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            titleIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "SystemFillColorCriticalBrush");
            var titleText = new TextBlock
            {
                Text = title, FontWeight = FontWeights.SemiBold, FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center
            };
            titlePanel.Children.Add(titleIcon);
            titlePanel.Children.Add(titleText);

            var dialog = new ContentDialog
            {
                Title = titlePanel,
                Content = textBox,
                CloseButtonText = "閉じる"
            };
            await WpfSyncConfirmationService.ShowRestoringFocusAsync(contentDialogs, dialog, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // 既に別のダイアログを表示中などで表示できない場合は無視する(エラー自体は呼び出し元でログ済み)。
        }
        finally
        {
            onClosed?.Invoke();
        }
    }
}
