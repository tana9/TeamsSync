namespace TeamsSync.Domain.Teams;

// 差分一覧画面(SyncChangeRowViewModel)に加え、監査CSV(SyncResultWriter)の
// 「チームメンバー変更一覧」でも同じ日本語化が必要になったため、Presentation層から
// ChangeKindTextと同じDomain.Teamsへ移した
/// <summary>同期差分の理由コードを利用者向けの日本語へ変換する</summary>
public static class SyncChangeReasonText
{
    /// <summary>理由コードに対応する日本語の説明文を返す</summary>
    public static string Format(ChangeReason reason)
    {
        return reason switch
        {
            ChangeReason.AmbiguousCurrentMember => "同じ氏名のチームメンバーが複数いるため特定できません",
            ChangeReason.AmbiguousDirectoryUser => "同じ氏名のユーザーが複数いるため、メールアドレスで指定してください",
            ChangeReason.UserNotFound => "ユーザーが見つかりません",
            ChangeReason.OwnerProtected => "所有者のため削除しません",
            ChangeReason.RemoveSpecified => "指定された一般メンバーを削除します",
            ChangeReason.AlreadyMember => "既にメンバーです",
            ChangeReason.AlreadyMemberDifferentIdentifier => "別のアドレスで既にメンバーです",
            ChangeReason.NotCurrentMember => "現在このチームに所属していません",
            ChangeReason.AddToTeam => "メンバーに追加します",
            ChangeReason.RemoveNotInInput => "リストにないため削除します",
            ChangeReason.ManuallyExcluded => "個別に除外したため変更しません",
            ChangeReason.AlreadyMemberNameMatchOnly => "氏名のみの一致で既にメンバーと判定しました(メールアドレスでの確認を推奨)",
            ChangeReason.RemoveSpecifiedNameMatchOnly => "氏名のみの一致で削除対象と判定しました(メールアドレスでの確認を推奨)",
            _ => ""
        };
    }
}
