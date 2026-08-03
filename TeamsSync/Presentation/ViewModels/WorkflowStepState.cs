namespace TeamsSync.Presentation.ViewModels;

// 画面上に番号付きで並ぶ4枚のカード(1 チーム選択 → 2 メンバーリスト → 3 同期モード → 4 同期差分)のうち、
// WorkflowStepsViewModelが進捗を算出するのは手順1〜3まで(手順4は手順3完了後に案内される差分確認・実行
// そのものであり、進捗はSyncWorkspaceViewModelのPlan/Resultが個別に管理する)。
// 各カード直下に表示するブロッカーメッセージ(Currentの間だけ表示する赤いエラーメッセージ)の
// 表示条件を計算するために使う
/// <summary>画面の操作手順1ステップ分の進捗状態</summary>
public enum WorkflowStepState
{
    /// <summary>まだ着手できない(前の手順が未完了)</summary>
    Upcoming,

    /// <summary>現在案内中の手順</summary>
    Current,

    /// <summary>完了した手順</summary>
    Completed
}