using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>同期実行、監査CSV保存、実行後の最新状態取得を1つのユースケースとして調整する</summary>
/// <remarks>分割されたプラン作成・同期実行サービスを使用してユースケースを構成する</remarks>
public sealed class SyncExecutionCoordinator(ISyncPlanService plans, ISyncExecutor executor,
    ISyncResultWriter resultWriter)
{
    /// <summary>実行直前にプレビュー済みプランを最新のメンバー構成で再検証する</summary>
    /// <param name="plan">再検証するプレビュー済みの同期プラン</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>最新の同期プランと、元のプランをそのまま実行できるかどうか</returns>
    public Task<SyncPlanRevalidation> RevalidateAsync(SyncPlan plan,
        CancellationToken cancellationToken = default)
    {
        return plans.RevalidatePlanAsync(plan, cancellationToken);
    }

    /// <summary>入力されたチームとアドレスから同期プランを作成する</summary>
    public Task<SyncPlan> BuildPlanAsync(TeamInfo team, IReadOnlyList<string> addresses,
        SyncMode mode, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        return plans.BuildPlanAsync(team, addresses, mode, progress, cancellationToken);
    }

    /// <summary>
    ///     同期プランを実行し、監査CSVの保存と実行後の最新状態取得を行う。
    ///     独立して失敗し得る後処理の結果も戻り値へ格納する
    /// </summary>
    /// <param name="plan">実行する同期プラン</param>
    /// <param name="auditContext">監査CSVへ記録する実行情報</param>
    /// <param name="progress">操作の進捗を受け取る通知先。通知が不要な場合はnull</param>
    /// <param name="reconciliationStarting">実行後の最新状態取得を開始する直前に呼び出す処理</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>同期結果、監査CSVの保存結果、および実行後の最新状態取得結果</returns>
    public async Task<SyncExecutionOutcome> ExecuteAsync(SyncPlan plan, SyncAuditContext auditContext,
        IProgress<SyncProgress>? progress = null, Action? reconciliationStarting = null,
        CancellationToken cancellationToken = default)
    {
        SyncExecutionResult execution = await executor.ExecuteAsync(
            plan, progress, auditContext, cancellationToken);

        string resultLogPath = "";
        Exception? logSaveError = null;
        try
        {
            resultLogPath = resultWriter.WriteAutoLog(plan, execution, auditContext.ExecutionId);
        }
        catch (Exception ex)
        {
            logSaveError = ex;
        }

        SyncPlan? remainingPlan = null;
        Exception? reconciliationError = null;
        try
        {
            reconciliationStarting?.Invoke();
            // キャンセル時も取りこぼしを検出するため、既にキャンセル済みの可能性があるトークンではなく
            // CancellationToken.Noneで最新状態を取得する。渡されたトークンをそのまま使うと、
            // キャンセル直後は最新状態の取得自体も即座に中断され、取りこぼしを検出できなくなる
            CancellationToken reconciliationToken = execution.Cancelled ? CancellationToken.None : cancellationToken;
            remainingPlan = await plans.ReconcileAsync(plan, reconciliationToken);
        }
        catch (Exception ex)
        {
            reconciliationError = ex;
        }

        return new SyncExecutionOutcome(plan, execution, resultLogPath,
            logSaveError, remainingPlan, reconciliationError);
    }

    /// <summary>
    ///     実行直前に最新のメンバー構成でプランを再検証し、変化がない場合に限り実行する。
    ///     プレビュー後にチーム構成が変化していた場合(所有者への昇格を含む)は実行せず、
    ///     最新のプランを返す。この判定を呼び出し元(ViewModel等)の順序に依存させず、
    ///     実行経路そのものに組み込むためのメソッド
    /// </summary>
    /// <param name="plan">実行しようとしている同期プラン</param>
    /// <param name="auditContext">監査CSVへ記録する実行情報</param>
    /// <param name="progress">操作の進捗を受け取る通知先。通知が不要な場合はnull</param>
    /// <param name="reconciliationStarting">実行後の最新状態取得を開始する直前に呼び出す処理</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>再検証結果と、実行できた場合はその結果</returns>
    public async Task<SyncExecutionAttempt> RevalidateAndExecuteAsync(SyncPlan plan, SyncAuditContext auditContext,
        IProgress<SyncProgress>? progress = null, Action? reconciliationStarting = null,
        CancellationToken cancellationToken = default)
    {
        SyncPlanRevalidation revalidation = await plans.RevalidatePlanAsync(plan, cancellationToken);
        if (!revalidation.IsCurrent)
        {
            return new SyncExecutionAttempt(revalidation.LatestPlan, null);
        }

        SyncExecutionOutcome outcome = await ExecuteAsync(
            revalidation.LatestPlan, auditContext, progress, reconciliationStarting, cancellationToken);
        return new SyncExecutionAttempt(revalidation.LatestPlan, outcome);
    }
}

/// <summary>同期ユースケースの実行結果と、独立して失敗し得る後処理の状態</summary>
/// <param name="ExecutedPlan">実行に使用した同期プラン</param>
/// <param name="Execution">同期操作の実行結果</param>
/// <param name="ResultLogPath">保存した監査CSVのパス。保存できなかった場合は空文字</param>
/// <param name="LogSaveError">監査CSVの保存エラー。保存に成功した場合はnull</param>
/// <param name="RemainingPlan">実行後の最新状態から作成した未反映分の同期プラン</param>
/// <param name="ReconciliationError">実行後の最新状態取得エラー。取得に成功した場合はnull</param>
public sealed record SyncExecutionOutcome(
    SyncPlan ExecutedPlan,
    SyncExecutionResult Execution,
    string ResultLogPath,
    Exception? LogSaveError,
    SyncPlan? RemainingPlan,
    Exception? ReconciliationError);

/// <summary>実行直前の再検証を経て、同期を実行できたかどうかを表す</summary>
/// <param name="LatestPlan">実行直前に再取得した最新の同期プラン</param>
/// <param name="Outcome">実行結果。チーム構成の変化により実行しなかった場合はnull</param>
public sealed record SyncExecutionAttempt(SyncPlan LatestPlan, SyncExecutionOutcome? Outcome)
{
    /// <summary>プレビュー後にチーム構成が変化しており、実行しなかった場合はtrue</summary>
    public bool IsStale => Outcome is null;
}