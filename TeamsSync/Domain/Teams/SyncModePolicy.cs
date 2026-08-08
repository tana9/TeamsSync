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
    ///     追加を許可せず削除は許可するモード(指定メンバー削除)でのみ削除対象とし、それ以外は
    ///     <paramref name="keepReason" />を理由に維持する。AllowsAdd/AllowsRemoveの両方を見て判定するため、
    ///     将来「追加も削除も許可しないモード」が追加されても、削除は許可されていないのに削除対象と
    ///     誤判定することはない。氏名のみによる一致(<paramref name="matchedByNameOnly" />)の場合は、
    ///     削除・維持のいずれでも確度が低いことが伝わる理由へ差し替える
    /// </summary>
    public (ChangeKind Kind, ChangeReason Reason) ClassifyMatchedNonOwner(ChangeReason keepReason,
        bool matchedByNameOnly)
    {
        if (!AllowsAdd && AllowsRemove)
        {
            return (ChangeKind.Remove,
                matchedByNameOnly ? ChangeReason.RemoveSpecifiedNameMatchOnly : ChangeReason.RemoveSpecified);
        }

        return (ChangeKind.Keep, matchedByNameOnly ? ChangeReason.AlreadyMemberNameMatchOnly : keepReason);
    }
}
