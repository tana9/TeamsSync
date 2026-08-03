using CommunityToolkit.Mvvm.ComponentModel;

using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>同期差分のプレビュー処理と進捗表示の状態を管理する</summary>
public sealed partial class SyncPreviewDisplayState : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial int ProgressMaximum { get; private set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial int ProgressValue { get; private set; }

    public string ProgressText =>
        SyncWorkspaceTextFormatter.BuildPreviewProgressText(ProgressValue, ProgressMaximum);

    public void SetBusy(bool value)
    {
        IsBusy = value;
    }

    public IProgress<int> Start(int total)
    {
        ProgressValue = 0;
        ProgressMaximum = Math.Max(1, total);
        return new Progress<int>(value => ProgressValue = value);
    }
}

/// <summary>Teamsへの同期実行と進捗表示の状態を管理する</summary>
public sealed partial class SyncExecutionDisplayState : ObservableObject
{
    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    public partial int ProgressMaximum { get; private set; } = 1;

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = "";

    [ObservableProperty]
    public partial int ProgressValue { get; private set; }

    public void Start(int total)
    {
        IsRunning = true;
        ProgressValue = 0;
        ProgressMaximum = Math.Max(1, total);
        ProgressText = "";
    }

    public string Report(SyncProgress progress)
    {
        ProgressValue = progress.Completed;
        ProgressMaximum = Math.Max(1, progress.Total);
        ProgressText =
            $"{progress.Completed} / {progress.Total}件 — {(progress.Kind == ChangeKind.Add ? "追加" : "削除")}中: {progress.Email}";
        return ProgressText;
    }

    public void Stop()
    {
        IsRunning = false;
    }
}
