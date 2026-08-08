namespace TeamsSync.Domain.Teams;

// ChangeKindの日本語ラベルへの変換が、監査CSV(SyncResultWriter)・差分一覧
// (SyncChangeRowViewModel)・実行結果一覧(SyncResultRowViewModel)の3箇所に別々のswitch/三項演算子として
// 重複していたため、変換ロジックをここへ集約した。呼び出し側で扱えるChangeKindの範囲が異なる
// (監査CSVは追加・削除のみ、差分一覧は全種別)ため、範囲外の値を渡した場合の扱いは呼び出し側に委ねる
/// <summary>同期差分の種別(<see cref="ChangeKind" />)を利用者向けの日本語ラベルへ変換する</summary>
public static class ChangeKindText
{
    /// <summary>種別に対応する日本語ラベルを返す</summary>
    public static string Label(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Add => "追加",
            ChangeKind.Remove => "削除",
            ChangeKind.Keep => "維持",
            ChangeKind.Protected => "所有者保護",
            ChangeKind.NotMember => "未所属",
            ChangeKind.Error => "エラー",
            ChangeKind.Excluded => "個別除外",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未対応の変更種別です。")
        };
    }
}
