using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;
using Wpf.Ui.Controls;

namespace TeamsSync.Presentation.ViewModels;

// SyncModeOption/ChangeFilter/SyncChangeRowViewModel/SyncResultRowViewModelは
// SyncWorkspaceModels.csへ、結果サマリー等の表示用テキスト組み立てはSyncWorkspaceTextFormatterへ
// 分離している。このクラスは同期実行ロジック(Graph API呼び出し・進捗・キャンセル)に専念する。
public partial class SyncWorkspaceViewModel : ObservableObject
{
    private readonly BusyOperationRunner _busyRunner;
    private readonly ISyncConfirmationService _confirmation;
    private readonly IFilePickerService _filePicker;
    private readonly INotificationService _notifications;
    private readonly IUserPreferences _preferences;
    private readonly ISyncResultWriter _resultWriter;
    private readonly TeamSyncService _syncService;
    private string? _actorObjectId;
    private MemberListDocument? _document;
    private bool _externallyBusy;
    private SyncPlan? _lastExecutedPlan;
    private SyncExecutionResult? _lastResult;
    private SyncPlan? _plan;
    private bool _signedIn;
    private CancellationTokenSource? _syncCancellation;
    private TaskCompletionSource? _syncCompletion;
    private TeamInfo? _team;
    private string? _tenantId;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRemovalWarningOpen;
    [ObservableProperty] private bool isSyncing;
    [ObservableProperty] private int previewProgressMaximum = 1;
    [ObservableProperty] private int previewProgressValue;
    [ObservableProperty] private int progressMaximum = 1;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private int progressValue;
    [ObservableProperty] private InfoBarSeverity removalSeverity = InfoBarSeverity.Warning;
    [ObservableProperty] private string removalWarningMessage = "";
    [ObservableProperty] private string removalWarningTitle = "";

    // 「3 同期モード」「差分確認・実行」の手順が完了しているかを画面の手順表示へ伝えるための状態。
    // HasPlanは差分確認済みか(モード変更で差分をクリアするとfalseに戻る)、
    // HasSyncResultは一度でも同期を実行したかを表す。
    [ObservableProperty] private bool hasPlan;
    [ObservableProperty] private bool hasSyncResult;

    // Snackbar(一時通知)が消えた後も成功・失敗・未反映件数を画面内に残すための実行結果サマリー。
    [ObservableProperty] private int resultSuccessCount;
    [ObservableProperty] private int resultFailureCount;
    [ObservableProperty] private bool resultCancelled;
    [ObservableProperty] private int resultRemainingCount = -1; // -1: 最終状態を未確認(再取得前または再取得失敗)

    [ObservableProperty] private ChangeFilter selectedFilter;
    [ObservableProperty] private SyncModeOption selectedMode;
    [ObservableProperty] private string summaryText = "";

    public SyncWorkspaceViewModel(TeamSyncService syncService, ISyncResultWriter resultWriter,
        IUserPreferences preferences, IFilePickerService filePicker,
        ISyncConfirmationService confirmation, INotificationService notifications)
    {
        _syncService = syncService;
        _resultWriter = resultWriter;
        _preferences = preferences;
        _filePicker = filePicker;
        _confirmation = confirmation;
        _notifications = notifications;
        _busyRunner = new BusyOperationRunner(_notifications,
            (message, isError) => StatusChanged?.Invoke(message, isError), value => IsBusy = value);
        selectedFilter = Filters[0];
        selectedMode = Modes[0];
        ChangesView = CollectionViewSource.GetDefaultView(Changes);
        ChangesView.Filter = FilterChange;
        Changes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChanges));
    }

    public ObservableCollection<SyncChangeRowViewModel> Changes { get; } = [];
    public ICollectionView ChangesView { get; }

    // 実行結果の失敗項目だけを持つ一覧。成功分を含めず「絞り込み済み」の状態で公開することで、
    // 差分DataGridの「エラーのみ表示」と同様に、失敗原因の確認に必要な項目だけを画面に残す。
    public ObservableCollection<SyncResultRowViewModel> FailedResults { get; } = [];

    public IReadOnlyList<SyncModeOption> Modes { get; } =
    [
        new(SyncMode.AddOnly, "追加のみ（既存メンバーを維持）"),
        new(SyncMode.FullSync, "完全同期（リスト外を削除）")
    ];

    public IReadOnlyList<ChangeFilter> Filters { get; } =
    [
        new("変更あり", null, true), new("すべて", null), new("追加", ChangeKind.Add),
        new("削除", ChangeKind.Remove), new("所有者", ChangeKind.Protected),
        new("エラー", ChangeKind.Error), new("変更なし", ChangeKind.Keep)
    ];

    public bool HasChanges => Changes.Count > 0;
    public bool HasErrors => Changes.Any(change => change.Kind == ChangeKind.Error);
    public string SyncUnavailableReason => GetSyncUnavailableReason();

    public bool HasFailedResults => FailedResults.Count > 0;
    public int ResultProcessedCount => ResultSuccessCount + ResultFailureCount;

    public string ResultSummaryText =>
        SyncWorkspaceTextFormatter.BuildResultSummaryText(ResultCancelled, ResultSuccessCount, ResultFailureCount);

    public bool HasResultRemainingCount => ResultRemainingCount >= 0;
    public string ResultRemainingText => SyncWorkspaceTextFormatter.BuildResultRemainingText(ResultRemainingCount);
    public string InputSummary => SyncWorkspaceTextFormatter.BuildInputSummary(_document);
    public string InputPreview => SyncWorkspaceTextFormatter.BuildInputPreview(_document);
    public bool IsNameColumn => SyncWorkspaceTextFormatter.IsNameColumn(_document);

    public string EmptyStateMessage =>
        SyncWorkspaceTextFormatter.BuildEmptyStateMessage(_signedIn, _team, _document);

    public string PreviewProgressText =>
        SyncWorkspaceTextFormatter.BuildPreviewProgressText(PreviewProgressValue, PreviewProgressMaximum);

    public bool IsAddOnlySelected
    {
        get => SelectedMode.Mode == SyncMode.AddOnly;
        set
        {
            if (value) SelectedMode = Modes[0];
        }
    }

    public bool IsFullSyncSelected
    {
        get => SelectedMode.Mode == SyncMode.FullSync;
        set
        {
            if (value) SelectedMode = Modes[1];
        }
    }

    public event Action<bool>? ActivityChanged;
    public event Action<string, bool>? StatusChanged;

    // 差分確認・再検証・同期後の再取得で一覧が更新されるたびに1回だけ発行する。
    // Viewはこれを受けて、差分の集計(エラーなし)または最初のエラー行(エラーあり)へフォーカスを移す。
    public event Action? DiffFocusRequested;

    public void SetContext(TeamInfo? team, MemberListDocument? document, bool signedIn,
        string? tenantId = null, string? actorObjectId = null)
    {
        if (_team == team && _document == document && _signedIn == signedIn &&
            _tenantId == tenantId && _actorObjectId == actorObjectId) return;
        _team = team;
        _document = document;
        _signedIn = signedIn;
        _tenantId = tenantId;
        _actorObjectId = actorObjectId;
        InvalidatePlan();
        NotifyCommandStates();
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(InputSummary));
        OnPropertyChanged(nameof(InputPreview));
        OnPropertyChanged(nameof(IsNameColumn));
    }

    public void SetExternalBusy(bool value)
    {
        _externallyBusy = value;
        NotifyCommandStates();
    }

    partial void OnSelectedFilterChanged(ChangeFilter value)
    {
        ChangesView.Refresh();
    }

    partial void OnSelectedModeChanged(SyncModeOption value)
    {
        OnPropertyChanged(nameof(IsAddOnlySelected));
        OnPropertyChanged(nameof(IsFullSyncSelected));
        var hadPlan = _plan is not null;
        InvalidatePlan();
        if (hadPlan)
            StatusChanged?.Invoke("同期モードを変更したため差分をクリアしました。「差分を確認」を再実行してください", false);
    }

    partial void OnIsBusyChanged(bool value)
    {
        ActivityChanged?.Invoke(value);
        NotifyCommandStates();
    }

    partial void OnIsSyncingChanged(bool value)
    {
        ActivityChanged?.Invoke(value);
        NotifyCommandStates();
    }

    partial void OnPreviewProgressValueChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewProgressText));
    }

    // ResultSummaryText/ResultProcessedCountは成功・失敗・中止件数から組み立てる計算プロパティのため、
    // 元になるObservablePropertyが変わるたびに変更通知を転送する。
    partial void OnResultSuccessCountChanged(int value)
    {
        NotifyResultSummary();
    }

    partial void OnResultFailureCountChanged(int value)
    {
        NotifyResultSummary();
    }

    partial void OnResultCancelledChanged(bool value)
    {
        NotifyResultSummary();
    }

    partial void OnResultRemainingCountChanged(int value)
    {
        OnPropertyChanged(nameof(ResultRemainingText));
        OnPropertyChanged(nameof(HasResultRemainingCount));
    }

    private void NotifyResultSummary()
    {
        OnPropertyChanged(nameof(ResultProcessedCount));
        OnPropertyChanged(nameof(ResultSummaryText));
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        await _busyRunner.RunAsync(async () =>
        {
            if (_team is null || _document is null) return;
            StatusChanged?.Invoke("ユーザーと現在のメンバーを照合しています…", false);
            PreviewProgressValue = 0;
            PreviewProgressMaximum = Math.Max(1, _document.Addresses.Count);
            var progress = new Progress<int>(count => PreviewProgressValue = count);
            var plan = await _syncService.BuildPlanAsync(_team, _document.Addresses,
                mode: SelectedMode.Mode, progress: progress);
            ApplyPlan(plan);
        });
    }

    private bool CanPreview()
    {
        return !_externallyBusy && !IsBusy && !IsSyncing &&
               _signedIn && _team is not null && _document is not null;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteSync))]
    private async Task ExecuteSyncAsync()
    {
        if (_plan is null || _document is null ||
            !await _confirmation.ConfirmSyncAsync(new SyncConfirmation(
                _plan, _document.FileName, $"{InputSummary}{Environment.NewLine}{InputPreview}"))) return;

        if (!await RevalidateBeforeExecuteAsync()) return;

        await RunSyncAndReconcileAsync();
    }

    // 実行直前にチームメンバーを再取得し、プレビュー作成後に構成が変わっていないか確認する。
    // falseを返す場合、最新の差分は既にApplyPlanで画面へ反映済みなので、呼び出し元は実行を中断するだけでよい。
    private async Task<bool> RevalidateBeforeExecuteAsync()
    {
        IsBusy = true;
        try
        {
            StatusChanged?.Invoke("実行直前のメンバー構成を確認しています…", false);
            var revalidation = await _syncService.RevalidatePlanAsync(_plan!);
            // ApplyPlan既定の案内文はこの後の分岐でより具体的なメッセージに置き換えるため、
            // 同じ内容をスクリーンリーダーへ二重に読み上げさせないようannounceStatus: falseで抑制する。
            ApplyPlan(revalidation.LatestPlan, announceStatus: false);
            if (revalidation.IsCurrent) return true;

            _notifications.ShowWarning("同期差分が変更されました",
                "チームのメンバー構成がプレビュー後に変更されました。最新の差分を確認して、もう一度同期を実行してください。");
            StatusChanged?.Invoke("最新の同期差分を表示しました。内容を再確認してください", true);
            return false;
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("処理を中止しました", false);
            return false;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("再検証に失敗しました。詳細はダイアログを確認してください", true);
            _notifications.ShowError(ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunSyncAndReconcileAsync()
    {
        _syncCancellation = new CancellationTokenSource();
        _syncCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IsSyncing = true;
        ProgressValue = 0;
        ProgressMaximum = Math.Max(1, _plan!.AddCount + _plan.RemoveCount);
        try
        {
            var progress = new Progress<SyncProgress>(ReportSyncProgress);
            _lastExecutedPlan = _plan;
            var auditContext = new SyncAuditContext(Guid.NewGuid(),
                _document!.FileName, _document.ContentSha256, _tenantId, _actorObjectId);
            _lastResult = await _syncService.ExecuteAsync(
                _plan, progress, _syncCancellation.Token, auditContext);
            HasSyncResult = true;
            UpdateResultCounts();

            if (_lastResult.Cancelled)
                HandleSyncCancelled();
            else
                await ReconcileAfterSyncAsync();

            SaveResultCommand.NotifyCanExecuteChanged();
            ExecuteSyncCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsSyncing = false;
            _syncCancellation.Dispose();
            _syncCancellation = null;
            _syncCompletion?.TrySetResult();
            _syncCompletion = null;
        }
    }

    private void ReportSyncProgress(SyncProgress progress)
    {
        ProgressValue = progress.Completed;
        ProgressMaximum = Math.Max(1, progress.Total);
        ProgressText =
            $"{progress.Completed} / {progress.Total}件 — {(progress.Kind == ChangeKind.Add ? "追加" : "削除")}中: {progress.Email}";
        StatusChanged?.Invoke(ProgressText, false);
    }

    private void HandleSyncCancelled()
    {
        // 中止までに着手した件数(_lastResult.Operations)を差し引いた残りを「未反映」として表示する。
        // Teams側の最新状態はまだ再取得していないため、件数は目安であることをResultRemainingTextの文言側で示す。
        ResultRemainingCount = Math.Max(0, _plan!.AddCount + _plan.RemoveCount - _lastResult!.Operations.Count);
        _plan = null;
        ExecuteSyncCommand.NotifyCanExecuteChanged();
        _notifications.ShowWarning("同期を中止しました",
            $"{_lastResult!.SuccessCount}件は処理済みです。差分を再確認してください。");
        StatusChanged?.Invoke("同期を中止しました。Teams側の最新状態を再確認してください", true);
    }

    // 実行後にTeams側の最終状態を再取得し、未反映分だけを新しい差分として表示する。
    // 再取得自体に失敗しても既に完了した操作結果は保存できるため、差分だけを無効化して継続可能にする。
    private async Task ReconcileAfterSyncAsync()
    {
        try
        {
            StatusChanged?.Invoke("Teams側の最新状態を確認しています…", false);
            var remaining = await _syncService.ReconcileAsync(_lastExecutedPlan!);
            // ApplyPlan既定の案内文はこの直後により具体的な完了メッセージへ置き換えるため、
            // 同じ内容をスクリーンリーダーへ二重に読み上げさせないようannounceStatus: falseで抑制する。
            ApplyPlan(remaining, announceStatus: false);
            var remainingCount = remaining.AddCount + remaining.RemoveCount;
            ResultRemainingCount = remainingCount;
            if (_lastResult!.FailureCount > 0)
                _notifications.ShowWarning("一部の操作に失敗しました",
                    $"成功 {_lastResult.SuccessCount}件 / 失敗 {_lastResult.FailureCount}件。未反映 {remainingCount}件を再実行できます。");
            else
                _notifications.ShowSuccess("同期完了",
                    $"{_lastResult.SuccessCount}件の変更が完了し、Teams側の状態を確認しました。");
            StatusChanged?.Invoke(remainingCount > 0
                ? $"最終状態を確認しました。未反映 {remainingCount}件を再実行できます"
                : "同期完了: Teams側の状態を確認済みです", remainingCount > 0);
        }
        catch (Exception ex)
        {
            InvalidatePlan(false);
            ResultRemainingCount = -1;
            _notifications.ShowWarning("最終状態を確認できませんでした",
                $"操作結果は保存できます。差分を再確認してください。{Environment.NewLine}{ex.Message}");
            StatusChanged?.Invoke("最終状態を確認できませんでした。差分を再確認してください", true);
        }
    }

    private bool CanExecuteSync()
    {
        return !_externallyBusy && !IsBusy && !IsSyncing &&
               _plan is { HasErrors: false } && (_plan.AddCount > 0 || _plan.RemoveCount > 0);
    }

    private string GetSyncUnavailableReason()
    {
        return SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(
            IsSyncing, IsBusy, _externallyBusy, _signedIn, _team, _document, _plan);
    }

    [RelayCommand]
    private void ShowErrors()
    {
        SelectedFilter = Filters.Single(filter => filter.Kind == ChangeKind.Error);
    }

    public async Task CancelAndWaitAsync()
    {
        var completion = _syncCompletion;
        _syncCancellation?.Cancel();
        if (completion is not null) await completion.Task;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _syncCancellation?.Cancel();
    }

    private bool CanCancel()
    {
        return IsSyncing && _syncCancellation is not null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveResult))]
    private void SaveResult()
    {
        if (_lastResult is null || _lastExecutedPlan is null) return;
        var path = _filePicker.PickResultFile(_preferences.LastFolder, _lastExecutedPlan.Team.DisplayName);
        if (path is null) return;
        try
        {
            _resultWriter.WriteCsv(path, _lastExecutedPlan, _lastResult);
            _preferences.LastFolder = Path.GetDirectoryName(path);
            try
            {
                _preferences.Save();
            }
            catch (Exception ex)
            {
                _notifications.ShowWarning("結果は保存しましたが、設定を保存できませんでした",
                    $"保存先: {path}{Environment.NewLine}{ex.Message}");
                return;
            }

            _notifications.ShowSuccess("結果を保存しました", path);
        }
        catch (Exception ex)
        {
            _notifications.ShowError(ex.Message, "結果を保存できませんでした");
        }
    }

    private bool CanSaveResult()
    {
        return _lastResult is not null && _lastExecutedPlan is not null &&
               !_externallyBusy && !IsBusy && !IsSyncing;
    }

    private void InvalidatePlan(bool clearResult = true)
    {
        _plan = null;
        HasPlan = false;
        Changes.Clear();
        SummaryText = "";
        IsRemovalWarningOpen = false;
        // 件数表示を未確認状態(-1)に戻す。理由はSyncWorkspaceTextFormatter.ClearFilterCounts参照。
        ClearFilterCounts();
        if (clearResult)
        {
            _lastResult = null;
            _lastExecutedPlan = null;
            HasSyncResult = false;
            ResultSuccessCount = 0;
            ResultFailureCount = 0;
            ResultCancelled = false;
            ResultRemainingCount = -1;
            FailedResults.Clear();
            OnPropertyChanged(nameof(HasFailedResults));
            SaveResultCommand.NotifyCanExecuteChanged();
        }

        ExecuteSyncCommand.NotifyCanExecuteChanged();
    }

    // 実行結果(_lastResult)から成功・失敗件数と失敗一覧を再構築する。同期完了直後に呼び出す。
    private void UpdateResultCounts()
    {
        ResultSuccessCount = _lastResult?.SuccessCount ?? 0;
        ResultFailureCount = _lastResult?.FailureCount ?? 0;
        ResultCancelled = _lastResult?.Cancelled ?? false;
        FailedResults.Clear();
        foreach (var operation in (_lastResult?.Operations ?? []).Where(operation => !operation.Succeeded))
            FailedResults.Add(new SyncResultRowViewModel(operation));
        OnPropertyChanged(nameof(HasFailedResults));
    }

    // announceStatus: falseの呼び出し元は、この直後に自分でより具体的な状況(再検証結果・完了結果など)を
    // StatusChangedへ通知する。既定の案内文と二重にスクリーンリーダーへ読み上げさせないための引数。
    private void ApplyPlan(SyncPlan plan, bool announceStatus = true)
    {
        _plan = plan;
        HasPlan = true;
        ReplaceChanges(plan.Changes);
        SummaryText =
            $"追加 {plan.AddCount} / 削除 {plan.RemoveCount} / 変更なし {plan.KeepCount} / 所有者 {plan.ProtectedCount} / エラー {plan.ErrorCount}";
        IsRemovalWarningOpen = plan.RemoveCount > 0;
        RemovalWarningTitle = plan.IsLargeRemoval ? "大量削除の可能性があります" : "削除対象があります";
        RemovalSeverity = plan.IsLargeRemoval ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
        RemovalWarningMessage = plan.IsLargeRemoval
            ? $"現在のメンバーの {plan.RemoveRatio:P0}（{plan.RemoveCount}名）を削除します。入力列と削除対象を確認してください。"
            : $"リストにない一般メンバー {plan.RemoveCount}名を削除します。";
        if (announceStatus)
        {
            StatusChanged?.Invoke(plan.HasErrors
                ? "未解決ユーザーがあります。リストを修正してください"
                : "差分を確認してください", plan.HasErrors);
            // 差分が更新されるたびに、エラーがあれば最初のエラーへ、なければ集計へフォーカスを移すようViewへ依頼する。
            // announceStatus=falseの呼び出し(再検証で差分が変わらない場合など)ではフォーカスも移動させない。
            // そうしないと、実行直前のフォーカス復元(ShowRestoringFocusAsync)をこのフォーカス移動が
            // 上書きしてしまう。
            DiffFocusRequested?.Invoke();
        }
        ExecuteSyncCommand.NotifyCanExecuteChanged();
    }

    private void ReplaceChanges(IEnumerable<SyncChange> changes)
    {
        Changes.Clear();
        foreach (var change in changes.OrderBy(x => x.Kind switch
                 {
                     ChangeKind.Error => 0, ChangeKind.Remove => 1, ChangeKind.Add => 2, ChangeKind.Protected => 3,
                     _ => 4
                 }))
            Changes.Add(new SyncChangeRowViewModel(change));
        ChangesView.Refresh();
        UpdateFilterCounts();
    }

    private void UpdateFilterCounts()
    {
        SyncWorkspaceTextFormatter.UpdateFilterCounts(Filters, Changes);
        CollectionViewSource.GetDefaultView(Filters).Refresh();
        OnPropertyChanged(nameof(HasErrors));
    }

    private void ClearFilterCounts()
    {
        SyncWorkspaceTextFormatter.ClearFilterCounts(Filters);
        CollectionViewSource.GetDefaultView(Filters).Refresh();
        OnPropertyChanged(nameof(HasErrors));
    }

    private bool FilterChange(object item)
    {
        if (item is not SyncChangeRowViewModel row) return false;
        if (SelectedFilter.ChangesOnly)
            return row.Kind is ChangeKind.Add or ChangeKind.Remove or ChangeKind.Error;
        return SelectedFilter.Kind is null || row.Kind == SelectedFilter.Kind;
    }

    private void NotifyCommandStates()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        ExecuteSyncCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        SaveResultCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SyncUnavailableReason));
    }
}
