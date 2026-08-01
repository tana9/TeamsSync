namespace TeamsSync.Presentation.ViewModels;

// 画面の操作手順(1 チーム選択 → 2 メンバーリスト → 3 同期モード → 4 同期差分)の進捗状態。
// 各カード直下に表示するブロッカーメッセージ(Currentの間だけ表示する赤いエラーメッセージ)の
// 表示条件を計算するために使う。
/// <summary>画面の操作手順1ステップ分の進捗状態。</summary>
public enum WorkflowStepState
{
    /// <summary>まだ着手できない(前の手順が未完了)。</summary>
    Upcoming,

    /// <summary>現在案内中の手順。</summary>
    Current,

    /// <summary>完了した手順。</summary>
    Completed
}