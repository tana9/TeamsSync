using TeamsSync.Application.Abstractions;
using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Application.Services;

/// <summary>同期実行、監査CSV保存、実行後の最新状態取得を1つのユースケースとして調整する</summary>
public sealed class SyncExecutionCoordinator
{
    private readonly ISyncExecutor _executor;
    private readonly ISyncPlanService _plans;
    private readonly ISyncResultWriter _resultWriter;

    /// <summary>分割されたプラン作成・同期実行サービスを使用してユースケースを構成する</summary>
    public SyncExecutionCoordinator(ISyncPlanService plans, ISyncExecutor executor, ISyncResultWriter resultWriter)
    {
        _plans = plans;
        _executor = executor;
        _resultWriter = resultWriter;
    }

    /// <summary>実行直前にプレビュー済みプランを最新のメンバー構成で再検証する</summary>
    /// <param name="plan">再検証するプレビュー済みの同期プラン</param>
    /// <param name="cancellationToken">処理のキャンセルを通知するトークン</param>
    /// <returns>最新の同期プランと、元のプランをそのまま実行できるかどうか</returns>
    public Task<SyncPlanRevalidation> RevalidateAsync(SyncPlan plan,
        CancellationToken cancellationToken = default)
    {
        return _plans.RevalidatePlanAsync(plan, cancellationToken);
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
        SyncExecutionResult execution = await _executor.ExecuteAsync(
            plan, progress, auditContext, cancellationToken);

        string resultLogPath = "";
        Exception? logSaveError = null;
        try
        {
            resultLogPath = _resultWriter.WriteAutoLog(plan, execution, auditContext.ExecutionId);
        }
        catch (Exception ex)
        {
            logSaveError = ex;
        }

        SyncPlan? remainingPlan = null;
        Exception? reconciliationError = null;
        if (!execution.Cancelled)
        {
            try
            {
                reconciliationStarting?.Invoke();
                remainingPlan = await _plans.ReconcileAsync(plan, cancellationToken);
            }
            catch (Exception ex)
            {
                reconciliationError = ex;
            }
        }

        return new SyncExecutionOutcome(plan, execution, resultLogPath,
            logSaveError, remainingPlan, reconciliationError);
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
