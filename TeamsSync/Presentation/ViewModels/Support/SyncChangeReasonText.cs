using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels.Support;

/// <summary>同期差分の理由コードを画面表示用の日本語へ変換する</summary>
internal static class SyncChangeReasonText
{
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
            _ => ""
        };
    }
}
