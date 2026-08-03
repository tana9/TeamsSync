using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using TeamsSync.Application.Models;
using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>直近の同期実行結果を画面に表示するための状態と派生値を管理する</summary>
public sealed partial class SyncResultDisplayState : ObservableObject
{
    private const int MaxVisibleFailedResults = 100;

    /// <summary>一度でも同期を実行したかどうか</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsRetry))]
    public partial bool HasResult { get; private set; }

    /// <summary>成功した操作の件数</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessedCount))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    public partial int SuccessCount { get; private set; }

    /// <summary>失敗した操作の件数</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessedCount))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    [NotifyPropertyChangedFor(nameof(HiddenFailedCount))]
    [NotifyPropertyChangedFor(nameof(HasHiddenFailedResults))]
    [NotifyPropertyChangedFor(nameof(HiddenFailedResultsText))]
    [NotifyPropertyChangedFor(nameof(NeedsRetry))]
    public partial int FailureCount { get; private set; }

    /// <summary>同期実行がキャンセルされたかどうか</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    [NotifyPropertyChangedFor(nameof(NeedsRetry))]
    public partial bool Cancelled { get; private set; }

    /// <summary>同期結果CSVのフルパス。保存できなかった場合は空文字</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLog))]
    public partial string LogPath { get; private set; } = "";

    /// <summary>Teams側に未反映の操作件数。最終状態を確認できない場合はnull</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemainingCount))]
    [NotifyPropertyChangedFor(nameof(RemainingText))]
    [NotifyPropertyChangedFor(nameof(NeedsRetry))]
    public partial int? RemainingCount { get; private set; }

    /// <summary>失敗した操作のうち画面へ表示する先頭項目</summary>
    public ObservableCollection<SyncResultRowViewModel> FailedResults { get; } = [];

    /// <summary>同期結果CSVが保存されているかどうか</summary>
    public bool HasLog => !string.IsNullOrWhiteSpace(LogPath);

    /// <summary>失敗した操作が1件以上あるかどうか</summary>
    public bool HasFailedResults => FailedResults.Count > 0;

    /// <summary>画面では省略し、結果CSVで確認できる失敗件数</summary>
    public int HiddenFailedCount => Math.Max(0, FailureCount - FailedResults.Count);

    /// <summary>画面で省略した失敗結果があるかどうか</summary>
    public bool HasHiddenFailedResults => HiddenFailedCount > 0;

    /// <summary>失敗結果を省略表示していることを案内するテキスト</summary>
    public string HiddenFailedResultsText =>
        $"ほか {HiddenFailedCount}件。すべての結果は同期結果CSVで確認できます。";

    /// <summary>失敗・キャンセル・未反映があり、最新状態から再プレビューする必要があるかどうか</summary>
    public bool NeedsRetry => HasResult && (FailureCount > 0 || Cancelled || RemainingCount > 0);

    /// <summary>処理済みの操作件数</summary>
    public int ProcessedCount => SuccessCount + FailureCount;

    /// <summary>同期実行結果の要約テキスト</summary>
    public string SummaryText =>
        SyncWorkspaceTextFormatter.BuildResultSummaryText(Cancelled, SuccessCount, FailureCount);

    /// <summary>未反映件数を確認できたかどうか</summary>
    public bool HasRemainingCount => RemainingCount.HasValue;

    /// <summary>未反映件数を示すテキスト</summary>
    public string RemainingText => SyncWorkspaceTextFormatter.BuildResultRemainingText(RemainingCount);

    /// <summary>新しい同期実行の開始前に、前回のログパスだけをクリアする</summary>
    public void BeginExecution()
    {
        LogPath = "";
    }

    /// <summary>同期実行結果とログパスを表示状態へ反映する</summary>
    public void Apply(SyncExecutionResult execution, string logPath)
    {
        SuccessCount = execution.SuccessCount;
        FailureCount = execution.FailureCount;
        Cancelled = execution.Cancelled;
        LogPath = logPath;
        HasResult = true;

        FailedResults.Clear();
        foreach (SyncResultRowViewModel row in SyncWorkspaceTextFormatter.BuildFailedRows(
                     execution, MaxVisibleFailedResults))
        {
            FailedResults.Add(row);
        }

        NotifyFailedResultsChanged();
    }

    /// <summary>Teams側の最終状態から確認した未反映件数を設定する</summary>
    public void SetRemainingCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        RemainingCount = count;
    }

    /// <summary>Teams側の最終状態を確認できなかったことを記録する</summary>
    public void MarkRemainingCountUnavailable()
    {
        RemainingCount = null;
    }

    /// <summary>直近の同期結果をすべてクリアする</summary>
    public void Clear()
    {
        HasResult = false;
        SuccessCount = 0;
        FailureCount = 0;
        Cancelled = false;
        LogPath = "";
        RemainingCount = null;
        FailedResults.Clear();
        NotifyFailedResultsChanged();
    }

    private void NotifyFailedResultsChanged()
    {
        OnPropertyChanged(nameof(HasFailedResults));
        OnPropertyChanged(nameof(HiddenFailedCount));
        OnPropertyChanged(nameof(HasHiddenFailedResults));
        OnPropertyChanged(nameof(HiddenFailedResultsText));
    }
}