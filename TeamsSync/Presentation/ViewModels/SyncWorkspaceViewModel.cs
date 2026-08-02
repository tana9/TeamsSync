using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

// SyncModeOption/ChangeFilter/SyncChangeRowViewModel/SyncResultRowViewModelは
// SyncWorkspaceModels.csへ、結果サマリー等の表示用テキスト組み立てはSyncWorkspaceTextFormatterへ
// 分離している。このクラスは同期実行ロジック(Graph API呼び出し・進捗・キャンセル)に専念する。
/// <summary>
///     同期モードの選択、差分プレビュー、同期の実行・キャンセル・結果保存を管理する
///     同期ワークスペース画面のViewModel。
/// </summary>
public partial class SyncWorkspaceViewModel : ObservableObject
{
    private const int MaxVisibleFailedResults = 100;
    private readonly BusyOperationRunner _busyRunner;
    private readonly ISyncConfirmationService _confirmation;
    private readonly INotificationService _notifications;
    private readonly SyncExecutionCoordinator _executionCoordinator;
    private readonly ISavedFileLauncher _savedFileLauncher;
    private readonly TeamSyncService _syncService;
    private readonly BulkObservableCollection<SyncChangeRowViewModel> _changes = [];
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

    /// <summary>コンストラクター。差分一覧の絞り込みビューと既定の選択状態を初期化する。</summary>
    public SyncWorkspaceViewModel(TeamSyncService syncService, ISyncResultWriter resultWriter,
        ISyncConfirmationService confirmation, INotificationService notifications,
        ISavedFileLauncher? savedFileLauncher = null, SyncExecutionCoordinator? executionCoordinator = null)
    {
        _syncService = syncService;
        _executionCoordinator = executionCoordinator ?? new SyncExecutionCoordinator(syncService, resultWriter);
        _confirmation = confirmation;
        _notifications = notifications;
        _savedFileLauncher = savedFileLauncher ?? UnavailableSavedFileLauncher.Instance;
        _busyRunner = new BusyOperationRunner(_notifications,
            (message, isError) => StatusChanged?.Invoke(message, isError), value => IsBusy = value);
        ChangesView = CollectionViewSource.GetDefaultView(Changes);
        ChangesView.Filter = FilterChange;
        Changes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChanges));
        SelectedFilter = Filters[0];
        SelectedMode = Modes[0];
    }

    /// <summary>差分確認・再検証などの処理中かどうか。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryRemainingCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>削除警告(InfoBar)を表示中かどうか。</summary>
    [ObservableProperty]
    public partial bool IsRemovalWarningOpen { get; set; }

    /// <summary>同期の実行中かどうか。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryRemainingCommand))]
    public partial bool IsSyncing { get; set; }

    /// <summary>差分確認の進捗の最大値。</summary>
    [ObservableProperty]
    public partial int PreviewProgressMaximum { get; set; } = 1;

    /// <summary>差分確認の進捗の現在値。</summary>
    [ObservableProperty]
    public partial int PreviewProgressValue { get; set; }

    /// <summary>同期実行の進捗の最大値。</summary>
    [ObservableProperty]
    public partial int ProgressMaximum { get; set; } = 1;

    /// <summary>同期実行中の進捗テキスト。</summary>
    [ObservableProperty]
    public partial string ProgressText { get; set; } = "";

    /// <summary>同期実行の進捗の現在値。</summary>
    [ObservableProperty]
    public partial int ProgressValue { get; set; }

    /// <summary>削除警告InfoBarの本文。</summary>
    [ObservableProperty]
    public partial string RemovalWarningMessage { get; set; } = "";

    /// <summary>削除警告InfoBarのタイトル。</summary>
    [ObservableProperty]
    public partial string RemovalWarningTitle { get; set; } = "";

    // 「3 同期モード」「差分確認・実行」の手順が完了しているかを画面の手順表示へ伝えるための状態。
    // HasPlanは差分確認済みか(モード変更で差分をクリアするとfalseに戻る)、
    // HasSyncResultは一度でも同期を実行したかを表す。
    /// <summary>差分確認済みかどうか(同期モード変更でfalseに戻る)。</summary>
    [ObservableProperty]
    public partial bool HasPlan { get; set; }

    /// <summary>一度でも同期を実行したかどうか。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryResult))]
    [NotifyCanExecuteChangedFor(nameof(RetryRemainingCommand))]
    public partial bool HasSyncResult { get; set; }

    // Snackbar(一時通知)が消えた後も成功・失敗・未反映件数を画面内に残すための実行結果サマリー。
    /// <summary>直近の同期実行における成功件数。</summary>
    [ObservableProperty]
    public partial int ResultSuccessCount { get; set; }

    /// <summary>直近の同期実行における失敗件数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryResult))]
    [NotifyCanExecuteChangedFor(nameof(RetryRemainingCommand))]
    public partial int ResultFailureCount { get; set; }

    /// <summary>直近の同期実行がキャンセルされたかどうか。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryResult))]
    [NotifyCanExecuteChangedFor(nameof(RetryRemainingCommand))]
    public partial bool ResultCancelled { get; set; }

    /// <summary>直近の同期実行結果を保存したCSVファイルのフルパス。保存失敗時は空文字。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultLog))]
    [NotifyCanExecuteChangedFor(nameof(OpenResultLogCommand))]
    public partial string ResultLogPath { get; set; } = "";

    /// <summary>直近の同期結果CSVが正常に保存されたかどうか。</summary>
    public bool HasResultLog => !string.IsNullOrWhiteSpace(ResultLogPath);

    /// <summary>同期後もTeams側に反映が残っている件数。-1は最終状態を未確認であることを表す。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryResult))]
    [NotifyCanExecuteChangedFor(nameof(RetryRemainingCommand))]
    public partial int ResultRemainingCount { get; set; } = -1; // -1: 最終状態を未確認(再取得前または再取得失敗)

    /// <summary>差分一覧に適用中の絞り込みフィルター。</summary>
    [ObservableProperty]
    public partial ChangeFilter SelectedFilter { get; set; }

    /// <summary>選択中の同期モード。</summary>
    [ObservableProperty]
    public partial SyncModeOption SelectedMode { get; set; }

    /// <summary>差分件数の要約テキスト。</summary>
    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    /// <summary>差分一覧の行データ。</summary>
    public ObservableCollection<SyncChangeRowViewModel> Changes => _changes;

    /// <summary>絞り込みフィルターを適用した<see cref="Changes" />のビュー。</summary>
    public ICollectionView ChangesView { get; }

    // 実行結果の失敗項目だけを持つ一覧。成功分を含めず「絞り込み済み」の状態で公開することで、
    // 差分DataGridの「エラーのみ表示」と同様に、失敗原因の確認に必要な項目だけを画面に残す。
    /// <summary>直近の同期実行のうち、失敗した操作だけの一覧。</summary>
    public ObservableCollection<SyncResultRowViewModel> FailedResults { get; } = [];

    /// <summary>同期モード選択コンボボックスの選択肢。</summary>
    public IReadOnlyList<SyncModeOption> Modes { get; } =
    [
        new(SyncMode.AddOnly, SyncWorkspaceTextFormatter.BuildModeLabel(SyncMode.AddOnly)),
        new(SyncMode.RemoveSpecified, SyncWorkspaceTextFormatter.BuildModeLabel(SyncMode.RemoveSpecified)),
        new(SyncMode.FullSync, SyncWorkspaceTextFormatter.BuildModeLabel(SyncMode.FullSync))
    ];

    /// <summary>差分一覧の絞り込みフィルターの選択肢。</summary>
    public IReadOnlyList<ChangeFilter> Filters { get; } =
    [
        new("変更あり", null, true), new("すべて", null), new("追加", ChangeKind.Add),
        new("削除", ChangeKind.Remove), new("所有者", ChangeKind.Protected),
        new("未所属", ChangeKind.NotMember),
        new("エラー", ChangeKind.Error), new("変更なし", ChangeKind.Keep)
    ];

    /// <summary>差分一覧に1件以上の項目があるかどうか。</summary>
    public bool HasChanges => Changes.Count > 0;

    /// <summary>差分一覧に未解決(エラー)の項目があるかどうか。</summary>
    public bool HasErrors => Changes.Any(change => change.Kind == ChangeKind.Error);

    /// <summary>同期を実行できない理由の説明文(実行可能な場合はその旨)。</summary>
    public string SyncUnavailableReason => GetSyncUnavailableReason();

    /// <summary>失敗した操作が1件以上あるかどうか。</summary>
    public bool HasFailedResults => FailedResults.Count > 0;

    /// <summary>画面では省略し、保存済みCSVで確認できる失敗件数。</summary>
    public int HiddenFailedResultCount => Math.Max(0, ResultFailureCount - FailedResults.Count);

    /// <summary>画面で省略した失敗結果があるかどうか。</summary>
    public bool HasHiddenFailedResults => HiddenFailedResultCount > 0;

    /// <summary>失敗結果を省略表示していることを案内するテキスト。</summary>
    public string HiddenFailedResultsText =>
        $"ほか {HiddenFailedResultCount}件。すべての結果は同期結果CSVで確認できます。";

    /// <summary>失敗・キャンセル・未反映があり、最新状態から再プレビューできるかどうか。</summary>
    public bool CanRetryResult => HasSyncResult &&
                                  (ResultFailureCount > 0 || ResultCancelled || ResultRemainingCount > 0);

    /// <summary>直近の同期実行で処理済みの件数(成功+失敗)。</summary>
    public int ResultProcessedCount => ResultSuccessCount + ResultFailureCount;

    /// <summary>直近の同期実行結果の要約テキスト。</summary>
    public string ResultSummaryText =>
        SyncWorkspaceTextFormatter.BuildResultSummaryText(ResultCancelled, ResultSuccessCount, ResultFailureCount);

    /// <summary>未反映件数が確認済み(0以上)かどうか。</summary>
    public bool HasResultRemainingCount => ResultRemainingCount >= 0;

    /// <summary>未反映件数を示すテキスト。</summary>
    public string ResultRemainingText => SyncWorkspaceTextFormatter.BuildResultRemainingText(ResultRemainingCount);

    /// <summary>入力元・検出列・件数の要約テキスト。</summary>
    public string InputSummary => SyncWorkspaceTextFormatter.BuildInputSummary(_document);

    /// <summary>入力アドレスの先頭数件のプレビューテキスト。</summary>
    public string InputPreview => SyncWorkspaceTextFormatter.BuildInputPreview(_document);

    /// <summary>検出された列がメールアドレスではなく氏名の列かどうか。</summary>
    public bool IsNameColumn => SyncWorkspaceTextFormatter.IsNameColumn(_document);

    /// <summary>差分未確認時に表示する案内メッセージ。</summary>
    public string EmptyStateMessage =>
        SyncWorkspaceTextFormatter.BuildEmptyStateMessage(_signedIn, _team, _document);

    // 既定の「変更あり」フィルターは追加・削除・エラーのみを表示するため、全員が変更なしの場合は
    // 一覧が0行になる。HasChangesは絞り込み前の件数(変更なし行を含む)で判定しており、
    // このときはtrueのままでEmptyStateMessage側のオーバーレイは出ないため、
    // ただの空白の表にならないよう専用の案内を別途表示する。
    // 「変更なし」フィルターなどへ切り替えて実際に行が表示されているときは、案内を隠す必要があるため
    // 現在のフィルター後のビュー(ChangesView)が空かどうかもあわせて判定する。
    /// <summary>差分確認の結果、追加・削除ともに0件で新たに行う変更がなく、かつ現在のフィルターでは一覧に何も表示されないかどうか。</summary>
    public bool HasNoChangesToApply =>
        _plan is { HasErrors: false, AddCount: 0, RemoveCount: 0 } && !ChangesView.Cast<object>().Any();

    /// <summary>変更なし(同期済み)の場合に一覧領域へ表示する案内文。</summary>
    public string NoChangesMessage => _plan is null ? "" : SyncWorkspaceTextFormatter.BuildPreviewStatusText(_plan);

    /// <summary>差分確認中の進捗テキスト。</summary>
    public string PreviewProgressText =>
        SyncWorkspaceTextFormatter.BuildPreviewProgressText(PreviewProgressValue, PreviewProgressMaximum);

    /// <summary>ラジオボタン用に「追加のみ」モードが選択されているかどうかを表す。設定すると当該モードへ切り替える。</summary>
    public bool IsAddOnlySelected
    {
        get => SelectedMode.Mode == SyncMode.AddOnly;
        set
        {
            if (value)
            {
                SelectedMode = Modes[0];
            }
        }
    }

    /// <summary>ラジオボタン用に「完全同期」モードが選択されているかどうかを表す。設定すると当該モードへ切り替える。</summary>
    public bool IsFullSyncSelected
    {
        get => SelectedMode.Mode == SyncMode.FullSync;
        set
        {
            if (value)
            {
                SelectedMode = Modes.Single(mode => mode.Mode == SyncMode.FullSync);
            }
        }
    }

    /// <summary>ラジオボタン用に「指定メンバーを削除」モードが選択されているかどうかを表す。</summary>
    public bool IsRemoveSpecifiedSelected
    {
        get => SelectedMode.Mode == SyncMode.RemoveSpecified;
        set
        {
            if (value)
            {
                SelectedMode = Modes.Single(mode => mode.Mode == SyncMode.RemoveSpecified);
            }
        }
    }

    /// <summary>ステータスメッセージを通知するために発行される。</summary>
    public event Action<string, bool>? StatusChanged;

    // 差分確認・再検証・同期後の再取得で一覧が更新されるたびに1回だけ発行する。
    // Viewはこれを受けて、差分の集計(エラーなし)または最初のエラー行(エラーあり)へフォーカスを移す。
    /// <summary>差分一覧が更新され、フォーカス移動が必要なときに発行される。</summary>
    public event Action? DiffFocusRequested;

    /// <summary>選択中のチーム・メンバーリスト・サインイン状態を設定し、変化があれば差分を無効化する。</summary>
    public void SetContext(TeamInfo? team, MemberListDocument? document, bool signedIn,
        string? tenantId = null, string? actorObjectId = null)
    {
        if (_team == team && _document == document && _signedIn == signedIn &&
            _tenantId == tenantId && _actorObjectId == actorObjectId)
        {
            return;
        }

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

    /// <summary>他画面の処理中状態を反映し、関連コマンドの実行可否を再評価する。</summary>
    public void SetExternalBusy(bool value)
    {
        _externallyBusy = value;
        NotifyCommandStates();
    }

    /// <summary>選択フィルターの変更に応じて差分一覧のビューを再フィルターする。</summary>
    partial void OnSelectedFilterChanged(ChangeFilter value)
    {
        ChangesView.Refresh();
        OnPropertyChanged(nameof(HasNoChangesToApply));
    }

    /// <summary>同期モードの変更に応じて関連プロパティを通知し、既存の差分プランを無効化する。</summary>
    partial void OnSelectedModeChanged(SyncModeOption value)
    {
        OnPropertyChanged(nameof(IsAddOnlySelected));
        OnPropertyChanged(nameof(IsRemoveSpecifiedSelected));
        OnPropertyChanged(nameof(IsFullSyncSelected));
        bool hadPlan = _plan is not null;
        InvalidatePlan();
        if (hadPlan)
        {
            StatusChanged?.Invoke("同期モードを変更したため差分をクリアしました。「差分を確認」を再実行してください", false);
        }
    }

    /// <summary>処理中状態の変化に応じて関連コマンドの実行可否を再評価する。</summary>
    partial void OnIsBusyChanged(bool value)
    {
        NotifyCommandStates();
    }

    /// <summary>同期実行中状態の変化に応じて関連コマンドの実行可否を再評価する。</summary>
    partial void OnIsSyncingChanged(bool value)
    {
        NotifyCommandStates();
    }

    /// <summary>差分確認の進捗値の変化に応じて進捗テキストを更新する。</summary>
    partial void OnPreviewProgressValueChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewProgressText));
    }

    // ResultSummaryText/ResultProcessedCountは成功・失敗・中止件数から組み立てる計算プロパティのため、
    // 元になるObservablePropertyが変わるたびに変更通知を転送する。
    /// <summary>成功件数の変化を結果サマリーへ反映する。</summary>
    partial void OnResultSuccessCountChanged(int value)
    {
        NotifyResultSummary();
    }

    /// <summary>失敗件数の変化を結果サマリーへ反映する。</summary>
    partial void OnResultFailureCountChanged(int value)
    {
        NotifyResultSummary();
    }

    /// <summary>キャンセル状態の変化を結果サマリーへ反映する。</summary>
    partial void OnResultCancelledChanged(bool value)
    {
        NotifyResultSummary();
    }

    /// <summary>未反映件数の変化に応じて関連プロパティを通知する。</summary>
    partial void OnResultRemainingCountChanged(int value)
    {
        OnPropertyChanged(nameof(ResultRemainingText));
        OnPropertyChanged(nameof(HasResultRemainingCount));
    }

    /// <summary>結果サマリー関連の計算プロパティの変更を通知する。</summary>
    private void NotifyResultSummary()
    {
        OnPropertyChanged(nameof(ResultProcessedCount));
        OnPropertyChanged(nameof(ResultSummaryText));
    }

    /// <summary>入力アドレスと現メンバーを照合し、同期プランを作成して画面へ反映する。</summary>
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        await TryPreviewAsync();
    }

    /// <summary>プレビューを更新し、画面へ反映できた場合にtrueを返す。</summary>
    private async Task<bool> TryPreviewAsync()
    {
        if (_team is null || _document is null)
        {
            return false;
        }

        SyncPlan? plan = await _busyRunner.RunAsync(async () =>
        {
            StatusChanged?.Invoke("ユーザーと現在のメンバーを照合しています…", false);
            PreviewProgressValue = 0;
            PreviewProgressMaximum = Math.Max(1, _document.Addresses.Count);
            Progress<int> progress = new(count => PreviewProgressValue = count);
            return await _syncService.BuildPlanAsync(_team, _document.Addresses,
                mode: SelectedMode.Mode, progress: progress);
        });

        if (plan is not null)
        {
            ApplyPlan(plan);
            return true;
        }

        return false;
    }

    private bool CanPreview()
    {
        return !_externallyBusy && !IsBusy && !IsSyncing &&
               _signedIn && _team is not null && _document is not null;
    }

    /// <summary>確認ダイアログでの同意と実行直前の再検証を経てから、同期を実行する。</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteSync))]
    private async Task ExecuteSyncAsync()
    {
        _syncCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _busyRunner.RunAsync(async () =>
            {
                if (_plan is null || _document is null ||
                    !await _confirmation.ConfirmSyncAsync(new SyncConfirmation(
                        _plan, _document.FileName, $"{InputSummary}{Environment.NewLine}{InputPreview}")))
                {
                    return;
                }

                if (!await RevalidateBeforeExecuteAsync())
                {
                    return;
                }

                await RunSyncAndReconcileAsync();
            }, ex => new BusyOperationRunner.SpecificExceptionResult(
                "同期を実行できませんでした。通知の「詳細をコピー」から内容を確認できます",
                "同期を実行できませんでした"));
        }
        finally
        {
            _syncCompletion.TrySetResult();
            _syncCompletion = null;
        }
    }

    // 実行直前にチームメンバーを再取得し、プレビュー作成後に構成が変わっていないか確認する。
    // falseを返す場合、最新の差分は既にApplyPlanで画面へ反映済みなので、呼び出し元は実行を中断するだけでよい。
    // 呼び出し元のExecuteSyncAsyncが既に同じ_busyRunnerでIsBusy=trueにしている(唯一の呼び出し元、
    // 常にその中から呼ばれる前提)ため、ここでmanageBusyState: falseを指定しないと、この処理が
    // 完了した時点でIsBusyがfalseへ戻ってしまい、まだRunSyncAndReconcileAsyncが実行中にもかかわらず
    // 画面の入力カードや「同期を実行」ボタンが一瞬再操作可能になってしまう。
    private async Task<bool> RevalidateBeforeExecuteAsync()
    {
        return await _busyRunner.RunAsync(async () =>
        {
            StatusChanged?.Invoke("実行直前のメンバー構成を確認しています…", false);
            SyncPlanRevalidation revalidation = await _executionCoordinator.RevalidateAsync(_plan!);
            // ApplyPlan既定の案内文はこの後の分岐でより具体的なメッセージに置き換えるため、
            // 同じ内容をスクリーンリーダーへ二重に読み上げさせないようannounceStatus: falseで抑制する。
            ApplyPlan(revalidation.LatestPlan, false);
            if (revalidation.IsCurrent)
            {
                return true;
            }

            _notifications.ShowWarning("同期差分が変更されました",
                "チームのメンバー構成がプレビュー後に変更されました。最新の差分を確認して、もう一度同期を実行してください。");
            StatusChanged?.Invoke("最新の同期差分を表示しました。内容を再確認してください", true);
            return false;
        }, ex => new BusyOperationRunner.SpecificExceptionResult(
            "再検証に失敗しました。通知の「詳細をコピー」から内容を確認できます"),
            manageBusyState: false);
    }

    /// <summary>
    ///     同期プランを実行し、結果を画面へ反映する。キャンセルされた場合と正常終了した場合とで
    ///     後処理(<see cref="HandleSyncCancelled" />/<see cref="ReconcileAfterSyncAsync" />)を分ける。
    /// </summary>
    private async Task RunSyncAndReconcileAsync()
    {
        _syncCancellation = new CancellationTokenSource();
        IsSyncing = true;
        ResultLogPath = "";
        ProgressValue = 0;
        ProgressMaximum = Math.Max(1, _plan!.AddCount + _plan.RemoveCount);
        try
        {
            Progress<SyncProgress> progress = new(ReportSyncProgress);
            _lastExecutedPlan = _plan;
            SyncAuditContext auditContext = new(Guid.NewGuid(),
                _document!.FileName, _document.ContentSha256, _tenantId, _actorObjectId);
            SyncExecutionOutcome outcome = await _executionCoordinator.ExecuteAsync(
                _plan, auditContext, progress,
                () => StatusChanged?.Invoke("Teams側の最新状態を確認しています…", false),
                _syncCancellation.Token);
            _lastResult = outcome.Execution;
            ResultLogPath = outcome.ResultLogPath;
            HasSyncResult = true;
            UpdateResultCounts();
            if (outcome.LogSaveError is not null)
            {
                _notifications.ShowWarning("実行ログを保存できませんでした",
                    $"同期処理自体は完了しています。{Environment.NewLine}{outcome.LogSaveError.Message}");
            }

            if (_lastResult.Cancelled)
            {
                HandleSyncCancelled();
            }
            else
            {
                HandleReconciliation(outcome);
            }

            ExecuteSyncCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsSyncing = false;
            _syncCancellation.Dispose();
            _syncCancellation = null;
        }
    }

    /// <summary>同期実行の進捗を画面の進捗バー・進捗テキスト・ステータスへ反映する。</summary>
    private void ReportSyncProgress(SyncProgress progress)
    {
        ProgressValue = progress.Completed;
        ProgressMaximum = Math.Max(1, progress.Total);
        ProgressText =
            $"{progress.Completed} / {progress.Total}件 — {(progress.Kind == ChangeKind.Add ? "追加" : "削除")}中: {progress.Email}";
        StatusChanged?.Invoke(ProgressText, false);
    }

    /// <summary>同期のキャンセルを検知し、未反映件数の見積もりと通知を行う。</summary>
    private void HandleSyncCancelled()
    {
        // 中止までに着手した件数(_lastResult.Operations)を差し引いた残りを「未反映」として表示する。
        // Teams側の最新状態はまだ再取得していないため、件数は目安であることをResultRemainingTextの文言側で示す。
        ResultRemainingCount = Math.Max(0, _plan!.AddCount + _plan.RemoveCount - _lastResult!.Operations.Count);
        _plan = null;
        OnPropertyChanged(nameof(HasNoChangesToApply));
        OnPropertyChanged(nameof(NoChangesMessage));
        ExecuteSyncCommand.NotifyCanExecuteChanged();
        ShowResultNotification(true, "同期を中止しました",
            $"{_lastResult!.SuccessCount}件は処理済みです。差分を再確認してください。");
        StatusChanged?.Invoke("同期を中止しました。Teams側の最新状態を再確認してください", true);
    }

    // 実行後にTeams側の最終状態を再取得し、未反映分だけを新しい差分として表示する。
    // 再取得自体に失敗しても既に完了した操作結果は保存できるため、差分だけを無効化して継続可能にする。
    /// <summary>
    ///     同期完了後にTeams側の最終状態を再取得し、未反映分を新しい差分として表示する。
    ///     再取得に失敗しても既に完了した実行結果は保持したまま、差分だけを無効化する。
    /// </summary>
    private void HandleReconciliation(SyncExecutionOutcome outcome)
    {
        if (outcome.ReconciliationError is null && outcome.RemainingPlan is not null)
        {
            SyncPlan remaining = outcome.RemainingPlan;
            // ApplyPlan既定の案内文はこの直後により具体的な完了メッセージへ置き換えるため、
            // 同じ内容をスクリーンリーダーへ二重に読み上げさせないようannounceStatus: falseで抑制する。
            ApplyPlan(remaining, false);
            int remainingCount = remaining.AddCount + remaining.RemoveCount;
            ResultRemainingCount = remainingCount;
            if (_lastResult!.FailureCount > 0)
            {
                ShowResultNotification(true, "一部の操作に失敗しました",
                    $"成功 {_lastResult.SuccessCount}件 / 失敗 {_lastResult.FailureCount}件。未反映 {remainingCount}件を再実行できます。");
            }
            else
            {
                ShowResultNotification(false, "同期完了",
                    $"{_lastResult.SuccessCount}件の変更が完了し、Teams側の状態を確認しました。");
            }

            StatusChanged?.Invoke(remainingCount > 0
                ? $"最終状態を確認しました。未反映 {remainingCount}件を再実行できます"
                : "同期完了: Teams側の状態を確認済みです", remainingCount > 0);
            return;
        }

        InvalidatePlan(false);
        ResultRemainingCount = -1;
        ShowResultNotification(true, "最終状態を確認できませんでした",
            $"操作結果は保存済みです。差分を再確認してください。{Environment.NewLine}{outcome.ReconciliationError?.Message}");
        StatusChanged?.Invoke("最終状態を確認できませんでした。差分を再確認してください", true);
    }

    /// <summary>部分失敗やキャンセル後に、Graphの最新状態から未反映分を再計画する。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryRemaining))]
    private async Task RetryRemainingAsync()
    {
        if (await TryPreviewAsync())
        {
            StatusChanged?.Invoke("最新状態から未反映分を再プレビューしました", false);
        }
    }

    private bool CanRetryRemaining() => CanRetryResult && CanPreview();

    /// <summary>同期結果とログ保存を1つのSnackbarで通知し、保存成功時はCSVを開く操作を付ける。</summary>
    private void ShowResultNotification(bool warning, string title, string message)
    {
        if (!HasResultLog)
        {
            if (warning) _notifications.ShowWarning(title, message);
            else _notifications.ShowSuccess(title, message);
            return;
        }

        string messageWithLog = $"{message} 実行ログを保存しました。";
        Action openLog = () => OpenResultLogCommand.Execute(null);
        if (warning)
        {
            _notifications.ShowWarningWithAction(title, messageWithLog, "同期結果CSVを開く", openLog);
        }
        else
        {
            _notifications.ShowSuccessWithAction(title, messageWithLog, "同期結果CSVを開く", openLog);
        }
    }

    /// <summary>保存済みの同期結果CSVを既定のアプリケーションで開く。</summary>
    [RelayCommand(CanExecute = nameof(HasResultLog))]
    private void OpenResultLog()
    {
        try
        {
            _savedFileLauncher.Open(ResultLogPath);
        }
        catch (Exception ex)
        {
            _notifications.ShowWarning("同期結果CSVを開けませんでした", ex.Message);
        }
    }

    private bool CanExecuteSync()
    {
        return !_externallyBusy && !IsBusy && !IsSyncing &&
               _plan is { HasErrors: false } && (_plan.AddCount > 0 || _plan.RemoveCount > 0);
    }

    /// <summary>現在の状態から、同期を実行できない理由(または実行可能である旨)を判定する。</summary>
    private string GetSyncUnavailableReason()
    {
        return SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(
            IsSyncing, IsBusy, _externallyBusy, _signedIn, _team, _document, _plan);
    }

    /// <summary>差分一覧の絞り込みを「エラー」フィルターへ切り替える。</summary>
    [RelayCommand]
    private void ShowErrors()
    {
        SelectedFilter = Filters.Single(filter => filter.Kind == ChangeKind.Error);
    }

    /// <summary>実行中の同期をキャンセルし、後処理が完了するまで待機する。</summary>
    public async Task CancelAndWaitAsync()
    {
        TaskCompletionSource? completion = _syncCompletion;
        _syncCancellation?.Cancel();
        if (completion is not null)
        {
            await completion.Task;
        }
    }

    /// <summary>実行中の同期をキャンセルする。</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _syncCancellation?.Cancel();
    }

    private bool CanCancel()
    {
        return IsSyncing && _syncCancellation is not null;
    }

    /// <summary>
    ///     現在の差分プランを無効化する。<paramref name="clearResult" />がtrueの場合は
    ///     直近の実行結果もあわせてクリアする。
    /// </summary>
    private void InvalidatePlan(bool clearResult = true)
    {
        _plan = null;
        HasPlan = false;
        Changes.Clear();
        SummaryText = "";
        IsRemovalWarningOpen = false;
        OnPropertyChanged(nameof(HasNoChangesToApply));
        OnPropertyChanged(nameof(NoChangesMessage));
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
            NotifyFailedResultVisibility();
        }

        ExecuteSyncCommand.NotifyCanExecuteChanged();
    }

    // 実行結果(_lastResult)から成功・失敗件数と失敗一覧を再構築する。同期完了直後に呼び出す。
    /// <summary>実行結果から成功・失敗件数と失敗一覧を再構築する。</summary>
    private void UpdateResultCounts()
    {
        ResultSuccessCount = _lastResult?.SuccessCount ?? 0;
        ResultFailureCount = _lastResult?.FailureCount ?? 0;
        ResultCancelled = _lastResult?.Cancelled ?? false;
        FailedResults.Clear();
        foreach (SyncResultRowViewModel row in SyncWorkspaceTextFormatter.BuildFailedRows(
                     _lastResult, MaxVisibleFailedResults))
        {
            FailedResults.Add(row);
        }

        OnPropertyChanged(nameof(HasFailedResults));
        NotifyFailedResultVisibility();
    }

    private void NotifyFailedResultVisibility()
    {
        OnPropertyChanged(nameof(HiddenFailedResultCount));
        OnPropertyChanged(nameof(HasHiddenFailedResults));
        OnPropertyChanged(nameof(HiddenFailedResultsText));
    }

    // announceStatus: falseの呼び出し元は、この直後に自分でより具体的な状況(再検証結果・完了結果など)を
    // StatusChangedへ通知する。既定の案内文と二重にスクリーンリーダーへ読み上げさせないための引数。
    /// <summary>
    ///     作成した同期プランを画面へ反映する(差分一覧・件数サマリー・削除警告)。
    ///     <paramref name="announceStatus" />がfalseの場合、既定の案内文とフォーカス移動は行わない。
    /// </summary>
    private void ApplyPlan(SyncPlan plan, bool announceStatus = true)
    {
        _plan = plan;
        HasPlan = true;
        ReplaceChanges(plan.Changes);
        PlanPresentation presentation = SyncWorkspaceTextFormatter.BuildPlan(plan);
        SummaryText = presentation.Summary;
        IsRemovalWarningOpen = plan.RemoveCount > 0;
        RemovalWarningTitle = presentation.RemovalTitle;
        RemovalWarningMessage = presentation.RemovalMessage;
        OnPropertyChanged(nameof(HasNoChangesToApply));
        OnPropertyChanged(nameof(NoChangesMessage));
        if (announceStatus)
        {
            StatusChanged?.Invoke(SyncWorkspaceTextFormatter.BuildPreviewStatusText(plan), plan.HasErrors);
            // 変更あり・エラーありは差分一覧にその内容が表示されるため見た目で気づけるが、
            // 変更なし(既に同期済み)は一覧が実質空のままで手がかりが他にないため、Snackbarでも知らせる。
            if (!plan.HasErrors && plan.AddCount + plan.RemoveCount == 0)
            {
                _notifications.ShowSuccess("変更はありません", "すでに同期済みです。");
            }

            // 差分が更新されるたびに、エラーがあれば最初のエラーへ、なければ集計へフォーカスを移すようViewへ依頼する。
            // announceStatus=falseの呼び出し(再検証で差分が変わらない場合など)ではフォーカスも移動させない。
            // そうしないと、実行直前のフォーカス復元(ShowRestoringFocusAsync)をこのフォーカス移動が
            // 上書きしてしまう。
            DiffFocusRequested?.Invoke();
        }

        ExecuteSyncCommand.NotifyCanExecuteChanged();
    }

    /// <summary>差分一覧を種別順(エラー→削除→追加→所有者→その他)に並べ替えて差し替える。</summary>
    private void ReplaceChanges(IEnumerable<SyncChange> changes)
    {
        _changes.ReplaceAll(changes.OrderBy(x => x.Kind switch
            {
                ChangeKind.Error => 0, ChangeKind.Remove => 1, ChangeKind.Add => 2, ChangeKind.Protected => 3,
                _ => 4
            })
            .Select(change => new SyncChangeRowViewModel(change)));

        ChangesView.Refresh();
        UpdateFilterCounts();
    }

    /// <summary>差分一覧の内容からフィルターごとの該当件数を再計算する。</summary>
    private void UpdateFilterCounts()
    {
        SyncWorkspaceTextFormatter.UpdateFilterCounts(Filters, Changes);
        CollectionViewSource.GetDefaultView(Filters).Refresh();
        OnPropertyChanged(nameof(HasErrors));
    }

    /// <summary>フィルターの件数表示を未確認状態へ戻す。</summary>
    private void ClearFilterCounts()
    {
        SyncWorkspaceTextFormatter.ClearFilterCounts(Filters);
        CollectionViewSource.GetDefaultView(Filters).Refresh();
        OnPropertyChanged(nameof(HasErrors));
    }

    /// <summary>差分一覧の1項目が現在選択中のフィルター条件に一致するかどうかを判定する。</summary>
    private bool FilterChange(object item)
    {
        if (item is not SyncChangeRowViewModel row)
        {
            return false;
        }

        if (SelectedFilter.ChangesOnly)
        {
            return row.Kind is ChangeKind.Add or ChangeKind.Remove or ChangeKind.Error;
        }

        return SelectedFilter.Kind is null || row.Kind == SelectedFilter.Kind;
    }

    /// <summary>各コマンドの実行可否を再評価させ、同期不可理由のプロパティ変更を通知する。</summary>
    private void NotifyCommandStates()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        ExecuteSyncCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SyncUnavailableReason));
    }
}

/// <summary>テストなどでファイル起動を使用しない場合の安全な既定実装。</summary>
file sealed class UnavailableSavedFileLauncher : ISavedFileLauncher
{
    public static UnavailableSavedFileLauncher Instance { get; } = new();

    public void Open(string path)
    {
        throw new InvalidOperationException("保存済みファイルを開くサービスが構成されていません。");
    }
}
