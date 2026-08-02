namespace TeamsSync.Presentation.ViewModels;

/// <summary>
///     直前の実行をキャンセル・破棄してから新しい非同期操作を開始する、単発実行の
///     CancellationTokenSourceライフサイクル管理。処理中フラグの設定やコマンドの再評価通知は
///     呼び出し元の責務のまま、キャンセル・差し替え・破棄の定型処理だけを担う。
/// </summary>
internal sealed class RestartableCancellation
{
    private CancellationTokenSource? _current;

    /// <summary>現在実行中(Begin後、対応するEndが呼ばれる前)かどうか。</summary>
    public bool IsActive => _current is not null;

    /// <summary>
    ///     直前の実行をキャンセル・破棄し、新しい<see cref="CancellationTokenSource" />を開始する。
    ///     戻り値は呼び出し元のtry/finallyで保持し、完了時に<see cref="End" />へそのまま渡すこと。
    /// </summary>
    public CancellationTokenSource Begin()
    {
        _current?.Cancel();
        _current?.Dispose();
        CancellationTokenSource cts = new();
        _current = cts;
        return cts;
    }

    /// <summary>
    ///     <paramref name="cts" />がまだ最新の実行であれば管理対象から外してから破棄する。
    ///     Begin以降に新しい実行が始まっていた場合は、その新しい実行を巻き込まないよう
    ///     フィールドのクリアは行わず、渡された<paramref name="cts" />自体の破棄だけを行う。
    /// </summary>
    public void End(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_current, cts))
        {
            _current = null;
        }

        cts.Dispose();
    }

    /// <summary>実行中であればキャンセルする。</summary>
    public void Cancel()
    {
        _current?.Cancel();
    }
}
