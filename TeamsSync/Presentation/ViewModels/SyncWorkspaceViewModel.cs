using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Application.Services;
using TeamsSync.Domain.Teams;
using TeamsSync.Presentation.Services;
using TeamsSync.Presentation.ViewModels.Support;

namespace TeamsSync.Presentation.ViewModels;

// SyncModeOption/ChangeFilter/SyncChangeRowViewModel/SyncResultRowViewModelは
// SyncWorkspaceModels.csへ、結果サマリー等の表示用テキスト組み立てはSyncWorkspaceTextFormatterへ
// 分離している。このクラスは同期実行ロジック(Graph API呼び出し・進捗・キャンセル)に専念する
/// <summary>
///     同期モードの選択、差分プレビュー、同期の実行・キャンセル・結果保存を管理する
///     同期ワークスペース画面のViewModel
/// </summary>
public partial class SyncWorkspaceViewModel : ObservableObject
{
    private readonly BusyOperationRunner _busyRunner;
    private readonly BusyOperationRunner _executeBusyRunner;
    private readonly ISyncConfirmationService _confirmation;
    private readonly SyncExecutionCoordinator _executionCoordinator;
    private readonly INotificationService _notifications;
    private readonly ISavedFileLauncher _savedFileLauncher;
    private readonly SyncExecutionRunner _executionRunner;
    private readonly SyncResultPresenter _resultPresenter;
    private readonly ViewModelUiEvents _uiEvents = new();
    private readonly SyncWorkspaceContext _context = new();
    private bool _externallyBusy;
    private SyncOperationsResult? _lastResult;
    private CancellationTokenSource? _syncCancellation;
    private TaskCompletionSource? _syncCompletion;

    /// <summary>コンストラクター。差分一覧の絞り込みビューと既定の選択状態を初期化する</summary>
    public SyncWorkspaceViewModel(SyncExecutionCoordinator executionCoordinator,
        ISyncConfirmationService confirmation, INotificationService notifications,
        ISavedFileLauncher savedFileLauncher, IIdentifierGenerator? identifierGenerator = null)
    {
        _executionCoordinator = executionCoordinator;
        _confirmation = confirmation;
        _notifications = notifications;
        _savedFileLauncher = savedFileLauncher;
        _executionRunner = new SyncExecutionRunner(executionCoordinator,
            identifierGenerator ?? new IdentifierGenerator());
        _resultPresenter = new SyncResultPresenter(_notifications, () => Result.HasLog,
            () => OpenResultLogCommand.Execute(null));
        _busyRunner = new BusyOperationRunner(_notifications,
            (message, isError) => _uiEvents.Status(message, isError), Preview.SetBusy);
        // 確認ダイアログ・実行直前の再検証・実際の反映を1つの処理中区間として扱うため、
        // Preview.IsBusyとは別のExecution.IsPreparingを使う専用のBusyOperationRunnerを持つ。
        // Preview.IsBusyを共用すると、差分確認カードの「確認しています…」オーバーレイが
        // 実行中もそのまま(しかも古い進捗のまま)表示され続けてしまう
        _executeBusyRunner = new BusyOperationRunner(_notifications,
            (message, isError) => _uiEvents.Status(message, isError), Execution.SetPreparing);
        Plan.PropertyChanged += OnPlanPropertyChanged;
        Result.PropertyChanged += OnResultPropertyChanged;
        Preview.PropertyChanged += OnOperationStatePropertyChanged;
        Execution.PropertyChanged += OnOperationStatePropertyChanged;
        SelectedMode = Modes[0];
    }

    /// <summary>差分確認・再検証の表示状態</summary>
    public SyncPreviewDisplayState Preview { get; } = new();

    /// <summary>Teamsへの同期実行の表示状態</summary>
    public SyncExecutionDisplayState Execution { get; } = new();

    /// <summary>同期プラン、差分一覧、フィルター、削除警告の表示状態</summary>
    public SyncPlanDisplayState Plan { get; } = new();

    /// <summary>直近の同期実行結果を表示する状態</summary>
    public SyncResultDisplayState Result { get; } = new();

    /// <summary>選択中の同期モード</summary>
    [ObservableProperty]
    public partial SyncModeOption SelectedMode { get; set; }

    /// <summary>同期モード選択コンボボックスの選択肢</summary>
    public IReadOnlyList<SyncModeOption> Modes { get; } =
    [
        new(SyncMode.AddOnly, SyncWorkspaceTextFormatter.BuildModeLabel(SyncMode.AddOnly)),
        new(SyncMode.RemoveSpecified, SyncWorkspaceTextFormatter.BuildModeLabel(SyncMode.RemoveSpecified)),
        new(SyncMode.FullSync, SyncWorkspaceTextFormatter.BuildModeLabel(SyncMode.FullSync))
    ];

    /// <summary>同期を実行できない理由の説明文(実行可能な場合はその旨)</summary>
    public string SyncUnavailableReason => GetSyncUnavailableReason();

    /// <summary>失敗・キャンセル・未反映があり、最新状態から再プレビューできるかどうか</summary>
    public bool CanRetryResult => Result.NeedsRetry;

    /// <summary>入力元・検出列・件数の要約テキスト</summary>
    public string InputSummary => SyncWorkspaceTextFormatter.BuildInputSummary(_context.Document);

    /// <summary>入力アドレスの先頭数件のプレビューテキスト</summary>
    public string InputPreview => SyncWorkspaceTextFormatter.BuildInputPreview(_context.Document);

    /// <summary>検出された列がメールアドレスではなく氏名の列かどうか</summary>
    public bool IsNameColumn => _context.Document?.IsNameColumn ?? false;

    /// <summary>差分未確認時に表示する案内メッセージ</summary>
    public string EmptyStateMessage =>
        SyncWorkspaceTextFormatter.BuildEmptyStateMessage(_context.SignedIn, _context.Team, _context.Document);

    /// <summary>ラジオボタン用に「追加のみ」モードが選択されているかどうかを表す。設定すると当該モードへ切り替える</summary>
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

    /// <summary>ラジオボタン用に「完全同期」モードが選択されているかどうかを表す。設定すると当該モードへ切り替える</summary>
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

    /// <summary>ラジオボタン用に「指定メンバーを削除」モードが選択されているかどうかを表す</summary>
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

    /// <summary>ステータスメッセージを通知するために発行される</summary>
    public event Action<string, bool>? StatusChanged
    {
        add => _uiEvents.StatusChanged += value;
        remove => _uiEvents.StatusChanged -= value;
    }

    // 差分確認・再検証・同期後の再取得で一覧が更新されるたびに1回だけ発行する。
    // Viewはこれを受けて、差分の集計(エラーなし)または最初のエラー行(エラーあり)へフォーカスを移す
    /// <summary>差分一覧が更新され、フォーカス移動が必要なときに発行される</summary>
    public event Action? DiffFocusRequested
    {
        add => _uiEvents.FocusRequested += value;
        remove => _uiEvents.FocusRequested -= value;
    }

    /// <summary>選択中のチーム・メンバーリスト・サインイン状態を設定し、変化があれば差分を無効化する</summary>
    public void SetContext(TeamInfo? team, MemberListDocument? document, bool signedIn,
        string? tenantId = null, string? actorObjectId = null, string? actorDisplayName = null)
    {
        if (!_context.Update(team, document, signedIn, tenantId, actorObjectId, actorDisplayName))
        {
            return;
        }
        InvalidatePlan();
        NotifyCommandStates();
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(InputSummary));
        OnPropertyChanged(nameof(InputPreview));
        OnPropertyChanged(nameof(IsNameColumn));
    }

    /// <summary>他画面の処理中状態を反映し、関連コマンドの実行可否を再評価する</summary>
    public void SetExternalBusy(bool value)
    {
        _externallyBusy = value;
        NotifyCommandStates();
    }

    /// <summary>同期モードの変更に応じて関連プロパティを通知し、既存の差分プランを無効化する</summary>
    partial void OnSelectedModeChanged(SyncModeOption value)
    {
        OnPropertyChanged(nameof(IsAddOnlySelected));
        OnPropertyChanged(nameof(IsRemoveSpecifiedSelected));
        OnPropertyChanged(nameof(IsFullSyncSelected));
        bool hadPlan = Plan.Current is not null;
        InvalidatePlan();
        if (hadPlan)
        {
            _uiEvents.Status("同期モードを変更したため差分をクリアしました。「差分を確認」を再実行してください", false);
        }
    }

    /// <summary>プレビュー・同期実行状態の変化に応じて関連コマンドの実行可否を再評価する</summary>
    private void OnOperationStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SyncPreviewDisplayState.IsBusy)
            or nameof(SyncExecutionDisplayState.IsBusy))
        {
            NotifyCommandStates();
            RetryRemainingCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>結果表示状態の変化を、ViewModelが持つコマンドの実行可否へ反映する</summary>
    private void OnResultPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncResultDisplayState.NeedsRetry))
        {
            OnPropertyChanged(nameof(CanRetryResult));
            RetryRemainingCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(SyncResultDisplayState.HasLog))
        {
            OpenResultLogCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>入力アドレスと現メンバーを照合し、同期プランを作成して画面へ反映する</summary>
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        await TryPreviewAsync();
    }

    /// <summary>プレビューを更新し、画面へ反映できた場合にtrueを返す</summary>
    private async Task<bool> TryPreviewAsync()
    {
        if (_context.Team is null || _context.Document is null)
        {
            return false;
        }

        SyncPlan? plan = await _busyRunner.RunAsync(async () =>
        {
            _uiEvents.Status("ユーザーと現在のメンバーを照合しています…", false);
            IProgress<int> progress = Preview.Start(_context.Document.Addresses.Count);
            return await _executionCoordinator.BuildPlanAsync(_context.Team, _context.Document.Addresses,
                SelectedMode.Mode, progress);
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
        return BuildReadiness().CanPreview;
    }

    /// <summary>確認ダイアログでの同意と実行直前の再検証を経てから、同期を実行する</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteSync))]
    private async Task ExecuteSyncAsync()
    {
        _syncCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _executeBusyRunner.RunAsync(async () =>
            {
                if (Plan.Current is null || _context.Document is null ||
                    !await _confirmation.ConfirmSyncAsync(new SyncConfirmation(
                        Plan.Current, _context.Document.FileName, $"{InputSummary}{Environment.NewLine}{InputPreview}")))
                {
                    return;
                }

                if (!await RevalidateBeforeExecuteAsync())
                {
                    return;
                }

                await RunSyncAndReconcileAsync();
            }, ex => new BusyOperationRunner.SpecificExceptionResult(
                "チームへ反映できませんでした。通知の「詳細をコピー」から内容を確認できます",
                "チームへ反映できませんでした"));
        }
        finally
        {
            _syncCompletion.TrySetResult();
            _syncCompletion = null;
        }
    }

    /// <summary>
    ///     実行直前にチームメンバーを再取得し、プレビュー作成後にチーム構成が変化していないか検証する。
    ///     構成が変化していた場合は、最新の同期プランを画面へ反映する
    /// </summary>
    /// <returns>プレビュー時点の同期プランをそのまま実行できる場合はtrue。それ以外の場合はfalse</returns>
    private async Task<bool> RevalidateBeforeExecuteAsync()
    {
        return await _executeBusyRunner.RunAsync(async () =>
            {
                _uiEvents.Status("実行直前のメンバー構成を確認しています…", false);
                SyncPlanRevalidation revalidation = await _executionCoordinator.RevalidateAsync(Plan.Current!);
                ApplyPlanSilently(revalidation.LatestPlan);
                if (revalidation.IsCurrent)
                {
                    return true;
                }

                _notifications.ShowWarning("同期差分が変更されました",
                    "チームのメンバー構成がプレビュー後に変更されました。最新の差分を確認して、もう一度チームに反映してください。");
                _uiEvents.Status("最新の同期差分を表示しました。内容を再確認してください", true);
                return false;
            }, ex => new BusyOperationRunner.SpecificExceptionResult(
                "再検証に失敗しました。通知の「詳細をコピー」から内容を確認できます"),
            false);
    }

    /// <summary>
    ///     同期プランを実行し、結果を画面へ反映する。実行直前の再検証(<see cref="RevalidateBeforeExecuteAsync" />)
    ///     通過後もチーム構成が変化していた場合は<see cref="HandleStaleBeforeExecution" />で防御的に処理する
    /// </summary>
    private async Task RunSyncAndReconcileAsync()
    {
        _syncCancellation = new CancellationTokenSource();
        Execution.Start(Plan.Current!.AddCount + Plan.Current.RemoveCount);
        Result.BeginExecution();
        try
        {
            Progress<SyncProgress> progress = new(ReportSyncProgress);
            SyncExecutionAttempt attempt;
            try
            {
                attempt = await _executionRunner.RunAsync(
                    Plan.Current, _context.Document!, _context.TenantId, _context.ActorObjectId,
                    _context.ActorDisplayName, progress,
                    () => _uiEvents.Status("Teams側の最新状態を確認しています…", false),
                    _syncCancellation.Token);
            }
            catch (OperationCanceledException) when (_syncCancellation.IsCancellationRequested)
            {
                // 実行直前の内部再検証(RevalidateAndExecuteAsync内のRevalidatePlanAsync)中に
                // キャンセルされた場合。Teams側へはまだ何も送信していないため、実行結果の適用は不要で
                // 通常のキャンセル完了時と同様に案内するだけでよい。ここで捕捉しないと、呼び出し元の
                // _executeBusyRunner.RunAsyncはこの時点のcancellationTokenを持たないため
                // 「反映できませんでした」という誤ったエラー表示になってしまう
                _uiEvents.Status("同期を中止しました", true);
                ExecuteSyncCommand.NotifyCanExecuteChanged();
                return;
            }

            if (attempt.IsStale)
            {
                HandleStaleBeforeExecution(attempt.LatestPlan);
                ExecuteSyncCommand.NotifyCanExecuteChanged();
                return;
            }

            SyncExecutionOutcome outcome = attempt.Outcome!;
            _lastResult = outcome.Execution;
            Result.Apply(_lastResult, outcome.ResultLogPath);
            if (outcome.LogSaveError is not null)
            {
                _notifications.ShowWarning("実行ログを保存できませんでした",
                    $"同期処理自体は完了しています。{Environment.NewLine}{outcome.LogSaveError.Message}");
            }

            HandleReconciliation(outcome, _lastResult.Cancelled);
            ExecuteSyncCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            Execution.Stop();
            _syncCancellation.Dispose();
            _syncCancellation = null;
        }
    }

    /// <summary>同期実行の進捗を画面の進捗バー・進捗テキスト・ステータスへ反映する</summary>
    private void ReportSyncProgress(SyncProgress progress)
    {
        _uiEvents.Status(Execution.Report(progress), false);
    }

    /// <summary>
    ///     <see cref="RevalidateBeforeExecuteAsync" />通過後、実行に取り掛かる直前のわずかな時間差で
    ///     チーム構成が変化していた場合(所有者への昇格を含む)の防御的な処理。通常は到達しない
    /// </summary>
    private void HandleStaleBeforeExecution(SyncPlan latestPlan)
    {
        ApplyPlanSilently(latestPlan);
        _notifications.ShowWarning("同期差分が変更されました",
            "チームのメンバー構成が実行直前に変更されたため、同期を中断しました。最新の差分を確認して、もう一度チームに反映してください。");
        _uiEvents.Status("最新の同期差分を表示しました。内容を再確認してください", true);
    }

    /// <summary>
    ///     同期の実行後(完了・部分失敗・キャンセルのいずれの場合も)にTeams側の最終状態を再取得し、
    ///     未反映分を新しい差分として表示する。キャンセル時も取りこぼしを検出するため必ず再取得を試み、
    ///     再取得に失敗しても既に完了した実行結果は保持したまま、差分だけを無効化する
    /// </summary>
    private void HandleReconciliation(SyncExecutionOutcome outcome, bool cancelled)
    {
        if (outcome.ReconciliationError is null && outcome.RemainingPlan is not null)
        {
            SyncPlan remaining = outcome.RemainingPlan;
            ApplyPlanSilently(remaining);
            int remainingCount = remaining.AddCount + remaining.RemoveCount;
            Result.SetRemainingCount(remainingCount);
            if (cancelled)
            {
                _resultPresenter.ShowWarning("同期を中止しました",
                    $"{_lastResult!.SuccessCount}件は処理済みです。Teams側の最新状態を確認しました。未反映 {remainingCount}件を再実行できます。");
            }
            else if (_lastResult!.FailureCount > 0)
            {
                _resultPresenter.ShowWarning("一部の操作に失敗しました",
                    $"成功 {_lastResult.SuccessCount}件 / 失敗 {_lastResult.FailureCount}件。未反映 {remainingCount}件を再実行できます。");
            }
            else
            {
                _resultPresenter.ShowSuccess("同期完了",
                    $"{_lastResult.SuccessCount}件の変更が完了し、Teams側の状態を確認しました。");
            }

            _uiEvents.Status(cancelled
                ? $"同期を中止しました。Teams側の最新状態を確認しました。未反映 {remainingCount}件を再実行できます"
                : remainingCount > 0
                    ? $"最終状態を確認しました。未反映 {remainingCount}件を再実行できます"
                    : "同期完了: Teams側の状態を確認済みです", cancelled || remainingCount > 0);
            return;
        }

        InvalidatePlan(false);
        Result.MarkRemainingCountUnavailable();
        _resultPresenter.ShowWarning(cancelled ? "同期を中止しました" : "最終状態を確認できませんでした",
            cancelled
                ? $"{_lastResult!.SuccessCount}件は処理済みの可能性があります。Teams側の最新状態を確認できませんでした。差分を再確認してください。{Environment.NewLine}{outcome.ReconciliationError?.Message}"
                : $"操作結果は保存済みです。差分を再確認してください。{Environment.NewLine}{outcome.ReconciliationError?.Message}");
        _uiEvents.Status(cancelled
            ? "同期を中止しました。最終状態を確認できませんでした。差分を再確認してください"
            : "最終状態を確認できませんでした。差分を再確認してください", true);
    }

    /// <summary>部分失敗やキャンセル後に、Graphの最新状態から未反映分を再計画する</summary>
    [RelayCommand(CanExecute = nameof(CanRetryRemaining))]
    private async Task RetryRemainingAsync()
    {
        if (await TryPreviewAsync())
        {
            _uiEvents.Status("最新状態から未反映分を再プレビューしました", false);
        }
    }

    private bool CanRetryRemaining()
    {
        return CanRetryResult && CanPreview();
    }

    /// <summary>保存済みの同期結果CSVを既定のアプリケーションで開く</summary>
    [RelayCommand(CanExecute = nameof(CanOpenResultLog))]
    private void OpenResultLog()
    {
        try
        {
            _savedFileLauncher.Open(Result.LogPath);
        }
        catch (Exception ex)
        {
            _notifications.ShowWarning("同期結果CSVを開けませんでした", ex.Message);
        }
    }

    /// <summary>差分プラン状態の変化を、ViewModelが持つコマンドの実行可否へ反映する</summary>
    private void OnPlanPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SyncPlanDisplayState.HasPlan)
            or nameof(SyncPlanDisplayState.HasErrors))
        {
            ExecuteSyncCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SyncUnavailableReason));
        }
    }

    private bool CanOpenResultLog()
    {
        return Result.HasLog;
    }

    private bool CanExecuteSync()
    {
        return BuildReadiness().CanExecuteSync;
    }

    /// <summary>現在の状態から、同期を実行できない理由(または実行可能である旨)を判定する</summary>
    private string GetSyncUnavailableReason()
    {
        return BuildReadiness().UnavailableReason;
    }

    /// <summary>現在のbusy状態・サインイン状態・チーム・文書・プランから、実行可否判定用のモデルを組み立てる</summary>
    private SyncWorkspaceReadiness BuildReadiness()
    {
        return new SyncWorkspaceReadiness(_externallyBusy, Preview, Execution, _context, Plan.Current);
    }

    /// <summary>差分一覧の絞り込みを「エラー」フィルターへ切り替える</summary>
    [RelayCommand]
    private void ShowErrors()
    {
        Plan.SelectErrors();
    }

    /// <summary>実行中の同期をキャンセルし、後処理が完了するまで待機する</summary>
    public async Task CancelAndWaitAsync()
    {
        TaskCompletionSource? completion = _syncCompletion;
        _syncCancellation?.Cancel();
        if (completion is not null)
        {
            await completion.Task;
        }
    }

    /// <summary>実行中の同期をキャンセルする</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _syncCancellation?.Cancel();
    }

    private bool CanCancel()
    {
        return Execution.IsRunning && _syncCancellation is not null;
    }

    /// <summary>
    ///     現在の差分プランを無効化する。<paramref name="clearResult" />がtrueの場合は
    ///     直近の実行結果もあわせてクリアする
    /// </summary>
    private void InvalidatePlan(bool clearResult = true)
    {
        Plan.Clear();
        if (clearResult)
        {
            _lastResult = null;
            Result.Clear();
        }

        ExecuteSyncCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SyncUnavailableReason));
    }

    /// <summary>
    ///     作成した同期プランを画面へ反映する(差分一覧・件数サマリー・削除警告)。
    ///     ステータスを通知する場合は、差分一覧へのフォーカス移動も要求する
    /// </summary>
    /// <param name="plan">画面へ反映する同期プラン</param>
    /// <param name="announceStatus">
    ///     既定のステータスメッセージを通知し、差分一覧へのフォーカス移動を要求する場合はtrue。
    ///     呼び出し元が個別のステータスを通知する場合はfalse
    /// </param>
    // ApplyPlan既定の案内文を、この直後により具体的なメッセージ・通知に置き換える3箇所
    // (RevalidateBeforeExecuteAsync/HandleStaleBeforeExecution/HandleReconciliation)で使う。
    // 同じ内容をスクリーンリーダーへ二重に読み上げさせないよう、既定の案内は抑制する
    /// <summary>
    ///     同期プランを画面へ反映するが、既定のステータス通知・フォーカス移動は行わない。
    ///     呼び出し元がこの直後により具体的な通知を行う場合に使う
    /// </summary>
    private void ApplyPlanSilently(SyncPlan plan)
    {
        ApplyPlan(plan, false);
    }

    private void ApplyPlan(SyncPlan plan, bool announceStatus = true)
    {
        Plan.Apply(plan);
        if (announceStatus)
        {
            _uiEvents.Status(SyncWorkspaceTextFormatter.BuildPreviewStatusText(plan), plan.HasErrors);
            // 変更あり・エラーありは差分一覧にその内容が表示されるため見た目で気づけるが、
            // 変更なし(既に同期済み)は一覧が実質空のままで手がかりが他にないため、Snackbarでも知らせる
            if (plan.HasNoActionableChanges)
            {
                _notifications.ShowSuccess("変更はありません", "すでに同期済みです。");
            }

            // 差分が更新されるたびに、エラーがあれば最初のエラーへ、なければ集計へフォーカスを移すようViewへ依頼する。
            // announceStatus=falseの呼び出し(再検証で差分が変わらない場合など)ではフォーカスも移動させない。
            // そうしないと、実行直前のフォーカス復元(ShowRestoringFocusAsync)をこのフォーカス移動が
            // 上書きしてしまう
            _uiEvents.RequestFocus();
        }

        ExecuteSyncCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SyncUnavailableReason));
    }

    /// <summary>各コマンドの実行可否を再評価させ、同期不可理由のプロパティ変更を通知する</summary>
    private void NotifyCommandStates()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        ExecuteSyncCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SyncUnavailableReason));
    }
}