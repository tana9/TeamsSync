using System.ComponentModel;

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
// 分離している。このクラスは同期実行ロジック(Graph API呼び出し・進捗・キャンセル)に専念する
/// <summary>
///     同期モードの選択、差分プレビュー、同期の実行・キャンセル・結果保存を管理する
///     同期ワークスペース画面のViewModel
/// </summary>
public partial class SyncWorkspaceViewModel : ObservableObject
{
    private readonly BusyOperationRunner _busyRunner;
    private readonly ISyncConfirmationService _confirmation;
    private readonly SyncExecutionCoordinator _executionCoordinator;
    private readonly INotificationService _notifications;
    private readonly ISavedFileLauncher _savedFileLauncher;
    private readonly ISyncPlanService _syncService;
    private string? _actorObjectId;
    private MemberListDocument? _document;
    private bool _externallyBusy;
    private SyncPlan? _lastExecutedPlan;
    private SyncExecutionResult? _lastResult;
    private bool _signedIn;
    private CancellationTokenSource? _syncCancellation;
    private TaskCompletionSource? _syncCompletion;
    private TeamInfo? _team;
    private string? _tenantId;

    /// <summary>コンストラクター。差分一覧の絞り込みビューと既定の選択状態を初期化する</summary>
    public SyncWorkspaceViewModel(ISyncPlanService syncService, SyncExecutionCoordinator executionCoordinator,
        ISyncConfirmationService confirmation, INotificationService notifications,
        ISavedFileLauncher savedFileLauncher)
    {
        _syncService = syncService;
        _executionCoordinator = executionCoordinator;
        _confirmation = confirmation;
        _notifications = notifications;
        _savedFileLauncher = savedFileLauncher;
        _busyRunner = new BusyOperationRunner(_notifications,
            (message, isError) => StatusChanged?.Invoke(message, isError), Preview.SetBusy);
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
    public string InputSummary => SyncWorkspaceTextFormatter.BuildInputSummary(_document);

    /// <summary>入力アドレスの先頭数件のプレビューテキスト</summary>
    public string InputPreview => SyncWorkspaceTextFormatter.BuildInputPreview(_document);

    /// <summary>検出された列がメールアドレスではなく氏名の列かどうか</summary>
    public bool IsNameColumn => _document?.IsNameColumn ?? false;

    /// <summary>差分未確認時に表示する案内メッセージ</summary>
    public string EmptyStateMessage =>
        SyncWorkspaceTextFormatter.BuildEmptyStateMessage(_signedIn, _team, _document);

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
    public event Action<string, bool>? StatusChanged;

    // 差分確認・再検証・同期後の再取得で一覧が更新されるたびに1回だけ発行する。
    // Viewはこれを受けて、差分の集計(エラーなし)または最初のエラー行(エラーあり)へフォーカスを移す
    /// <summary>差分一覧が更新され、フォーカス移動が必要なときに発行される</summary>
    public event Action? DiffFocusRequested;

    /// <summary>選択中のチーム・メンバーリスト・サインイン状態を設定し、変化があれば差分を無効化する</summary>
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
            StatusChanged?.Invoke("同期モードを変更したため差分をクリアしました。「差分を確認」を再実行してください", false);
        }
    }

    /// <summary>プレビュー・同期実行状態の変化に応じて関連コマンドの実行可否を再評価する</summary>
    private void OnOperationStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SyncPreviewDisplayState.IsBusy)
            or nameof(SyncExecutionDisplayState.IsRunning))
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
        if (_team is null || _document is null)
        {
            return false;
        }

        SyncPlan? plan = await _busyRunner.RunAsync(async () =>
        {
            StatusChanged?.Invoke("ユーザーと現在のメンバーを照合しています…", false);
            IProgress<int> progress = Preview.Start(_document.Addresses.Count);
            return await _syncService.BuildPlanAsync(_team, _document.Addresses,
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
        return !_externallyBusy && !Preview.IsBusy && !Execution.IsRunning &&
               _signedIn && _team is not null && _document is not null;
    }

    /// <summary>確認ダイアログでの同意と実行直前の再検証を経てから、同期を実行する</summary>
    [RelayCommand(CanExecute = nameof(CanExecuteSync))]
    private async Task ExecuteSyncAsync()
    {
        _syncCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _busyRunner.RunAsync(async () =>
            {
                if (Plan.Current is null || _document is null ||
                    !await _confirmation.ConfirmSyncAsync(new SyncConfirmation(
                        Plan.Current, _document.FileName, $"{InputSummary}{Environment.NewLine}{InputPreview}")))
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
        return await _busyRunner.RunAsync(async () =>
            {
                StatusChanged?.Invoke("実行直前のメンバー構成を確認しています…", false);
                SyncPlanRevalidation revalidation = await _executionCoordinator.RevalidateAsync(Plan.Current!);
                // ApplyPlan既定の案内文はこの後の分岐でより具体的なメッセージに置き換えるため、
                // 同じ内容をスクリーンリーダーへ二重に読み上げさせないようannounceStatus: falseで抑制する
                ApplyPlan(revalidation.LatestPlan, false);
                if (revalidation.IsCurrent)
                {
                    return true;
                }

                _notifications.ShowWarning("同期差分が変更されました",
                    "チームのメンバー構成がプレビュー後に変更されました。最新の差分を確認して、もう一度チームに反映してください。");
                StatusChanged?.Invoke("最新の同期差分を表示しました。内容を再確認してください", true);
                return false;
            }, ex => new BusyOperationRunner.SpecificExceptionResult(
                "再検証に失敗しました。通知の「詳細をコピー」から内容を確認できます"),
            false);
    }

    /// <summary>
    ///     同期プランを実行し、結果を画面へ反映する。キャンセルされた場合と正常終了した場合とで
    ///     後処理をキャンセル時と正常終了時で分ける
    /// </summary>
    private async Task RunSyncAndReconcileAsync()
    {
        _syncCancellation = new CancellationTokenSource();
        Execution.Start(Plan.Current!.AddCount + Plan.Current.RemoveCount);
        Result.BeginExecution();
        try
        {
            Progress<SyncProgress> progress = new(ReportSyncProgress);
            _lastExecutedPlan = Plan.Current;
            SyncAuditContext auditContext = new(Guid.NewGuid(),
                _document!.FileName, _document.ContentSha256, _tenantId, _actorObjectId);
            SyncExecutionOutcome outcome = await _executionCoordinator.ExecuteAsync(
                Plan.Current, auditContext, progress,
                () => StatusChanged?.Invoke("Teams側の最新状態を確認しています…", false),
                _syncCancellation.Token);
            _lastResult = outcome.Execution;
            Result.Apply(_lastResult, outcome.ResultLogPath);
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
            Execution.Stop();
            _syncCancellation.Dispose();
            _syncCancellation = null;
        }
    }

    /// <summary>同期実行の進捗を画面の進捗バー・進捗テキスト・ステータスへ反映する</summary>
    private void ReportSyncProgress(SyncProgress progress)
    {
        StatusChanged?.Invoke(Execution.Report(progress), false);
    }

    /// <summary>同期のキャンセルを検知し、未反映件数の見積もりと通知を行う</summary>
    private void HandleSyncCancelled()
    {
        // 中止までに着手した件数(_lastResult.Operations)を差し引いた残りを「未反映」として表示する。
        // Teams側の最新状態はまだ再取得していないため、件数は目安であることをResultRemainingTextの文言側で示す
        Result.SetRemainingCount(Math.Max(0,
            Plan.Current!.AddCount + Plan.Current.RemoveCount - _lastResult!.Operations.Count));
        Plan.Clear();
        ExecuteSyncCommand.NotifyCanExecuteChanged();
        ShowResultNotification(true, "同期を中止しました",
            $"{_lastResult!.SuccessCount}件は処理済みです。差分を再確認してください。");
        StatusChanged?.Invoke("同期を中止しました。Teams側の最新状態を再確認してください", true);
    }

    /// <summary>
    ///     同期完了後にTeams側の最終状態を再取得し、未反映分を新しい差分として表示する。
    ///     再取得に失敗しても既に完了した実行結果は保持したまま、差分だけを無効化する
    /// </summary>
    private void HandleReconciliation(SyncExecutionOutcome outcome)
    {
        if (outcome.ReconciliationError is null && outcome.RemainingPlan is not null)
        {
            SyncPlan remaining = outcome.RemainingPlan;
            // ApplyPlan既定の案内文はこの直後により具体的な完了メッセージへ置き換えるため、
            // 同じ内容をスクリーンリーダーへ二重に読み上げさせないようannounceStatus: falseで抑制する
            ApplyPlan(remaining, false);
            int remainingCount = remaining.AddCount + remaining.RemoveCount;
            Result.SetRemainingCount(remainingCount);
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
        Result.MarkRemainingCountUnavailable();
        ShowResultNotification(true, "最終状態を確認できませんでした",
            $"操作結果は保存済みです。差分を再確認してください。{Environment.NewLine}{outcome.ReconciliationError?.Message}");
        StatusChanged?.Invoke("最終状態を確認できませんでした。差分を再確認してください", true);
    }

    /// <summary>部分失敗やキャンセル後に、Graphの最新状態から未反映分を再計画する</summary>
    [RelayCommand(CanExecute = nameof(CanRetryRemaining))]
    private async Task RetryRemainingAsync()
    {
        if (await TryPreviewAsync())
        {
            StatusChanged?.Invoke("最新状態から未反映分を再プレビューしました", false);
        }
    }

    private bool CanRetryRemaining()
    {
        return CanRetryResult && CanPreview();
    }

    /// <summary>同期結果とログ保存を1つのSnackbarで通知し、保存成功時はCSVを開く操作を付ける</summary>
    private void ShowResultNotification(bool warning, string title, string message)
    {
        if (!Result.HasLog)
        {
            if (warning)
            {
                _notifications.ShowWarning(title, message);
            }
            else
            {
                _notifications.ShowSuccess(title, message);
            }

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
        return !_externallyBusy && !Preview.IsBusy && !Execution.IsRunning && (Plan.Current?.CanExecute ?? false);
    }

    /// <summary>現在の状態から、同期を実行できない理由(または実行可能である旨)を判定する</summary>
    private string GetSyncUnavailableReason()
    {
        return SyncWorkspaceTextFormatter.BuildSyncUnavailableReason(
            Execution.IsRunning, Preview.IsBusy, _externallyBusy, _signedIn, _team, _document, Plan.Current);
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
            _lastExecutedPlan = null;
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
    private void ApplyPlan(SyncPlan plan, bool announceStatus = true)
    {
        Plan.Apply(plan);
        if (announceStatus)
        {
            StatusChanged?.Invoke(SyncWorkspaceTextFormatter.BuildPreviewStatusText(plan), plan.HasErrors);
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
            DiffFocusRequested?.Invoke();
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
