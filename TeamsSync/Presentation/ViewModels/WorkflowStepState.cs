namespace TeamsSync.Presentation.ViewModels;

// 画面の操作手順(1 チーム選択 → 2 メンバーリスト → 3 同期モード → 4 同期差分)の進捗状態。
// 各カード直下に表示するブロッカーメッセージ(Currentの間だけ表示する赤いエラーメッセージ)の
// 表示条件を計算するために使う。
public enum WorkflowStepState
{
    Upcoming,
    Current,
    Completed
}
