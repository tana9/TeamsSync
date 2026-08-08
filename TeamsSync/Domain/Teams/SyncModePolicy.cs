namespace TeamsSync.Domain.Teams;

// SyncModeへの分岐(許可される操作の判定、CSV用・UI用の表示ラベル、モード不整合時のエラー文言)が
// SyncPlan・SyncResultWriter・SyncWorkspaceTextFormatterへ別々のswitchとして重複していたため、
// モードごとの性質を1箇所へ集約する値オブジェクトへ切り出した。SyncMode自体は[InlineData]など
// コンパイル時定数を要する箇所で使われているため列挙型のまま維持し、振る舞いだけをこちらへ委譲する
/// <summary>同期モードごとに許可される操作・表示ラベル・不整合時の案内文をまとめた値オブジェクト</summary>
/// <param name="AllowsAdd">このモードで追加操作を含められるかどうか</param>
/// <param name="AllowsRemove">このモードで削除操作を含められるかどうか</param>
/// <param name="ShortLabel">監査CSVなど簡潔な表示に使うラベル</param>
/// <param name="DetailedLabel">モード選択画面など補足を含めた表示に使うラベル</param>
/// <param name="InconsistentOperationMessage">
///     許可されない操作(追加/削除)が含まれていた場合の案内文。両方許可されるモードでは使われない
/// </param>
public sealed record SyncModePolicy(
    bool AllowsAdd,
    bool AllowsRemove,
    string ShortLabel,
    string DetailedLabel,
    string InconsistentOperationMessage)
{
    /// <summary>指定した同期モードのポリシーを返す</summary>
    public static SyncModePolicy For(SyncMode mode)
    {
        return mode switch
        {
            SyncMode.AddOnly => new SyncModePolicy(true, false,
                "追加のみ", "追加のみ（既存メンバーを維持）", "追加のみモードではメンバーを削除できません。"),
            SyncMode.RemoveSpecified => new SyncModePolicy(false, true,
                "指定メンバーを削除", "指定メンバーを削除", "指定メンバー削除モードではメンバーを追加できません。"),
            SyncMode.FullSync => new SyncModePolicy(true, true,
                "完全同期", "完全同期（リスト外を削除）", ""),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未対応の同期モードです。")
        };
    }

    // 現行の3モードでは「追加を許可しない」のはRemoveSpecifiedだけ(AddOnly/FullSyncは常にAllowsAdd)
    // なので、!AllowsAddだけで「指定メンバー削除モードかどうか」を安全に判定できる。SyncPlanFactory側に
    // 直接mode == SyncMode.RemoveSpecifiedと書く代わりにこちらへ寄せることで、モード追加時の
    // 分岐漏れをSyncModePolicy.For側の1箇所に留められる
    /// <summary>
    ///     入力で解決できたが現在チームに所属していない新規ユーザーを、このモードでどう扱うかを返す。
    ///     追加を許可しないモードでは、削除対象でもないことを示すNotMemberとして扱う
    /// </summary>
    public (ChangeKind Kind, ChangeReason Reason) ClassifyNewUser()
    {
        return AllowsAdd
            ? (ChangeKind.Add, ChangeReason.AddToTeam)
            : (ChangeKind.NotMember, ChangeReason.NotCurrentMember);
    }

    /// <summary>
    ///     入力で解決できた、既にチームに所属している非所有者メンバーを、このモードでどう扱うかを返す。
    ///     追加を許可しないモードでのみ削除対象とし、それ以外は<paramref name="keepReason" />を理由に維持する。
    ///     氏名のみによる一致(<paramref name="matchedByNameOnly" />)の場合は、削除・維持のいずれでも
    ///     確度が低いことが伝わる理由へ差し替える
    /// </summary>
    public (ChangeKind Kind, ChangeReason Reason) ClassifyMatchedNonOwner(ChangeReason keepReason,
        bool matchedByNameOnly)
    {
        if (!AllowsAdd)
        {
            return (ChangeKind.Remove,
                matchedByNameOnly ? ChangeReason.RemoveSpecifiedNameMatchOnly : ChangeReason.RemoveSpecified);
        }

        return (ChangeKind.Keep, matchedByNameOnly ? ChangeReason.AlreadyMemberNameMatchOnly : keepReason);
    }
}
