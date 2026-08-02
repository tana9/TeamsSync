using System.ComponentModel;

using TeamsSync.Application.Models;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels;

// SyncWorkspaceViewModelが扱う画面表示用のモデル型。ロジックを持たないデータ表現のため、
// 同期実行ロジック本体(SyncWorkspaceViewModel.cs)とは別ファイルに分離している。

/// <summary>同期モード選択コンボボックスの1項目(モードと表示ラベルの組)。</summary>
/// <param name="Mode">同期モード。</param>
/// <param name="Label">画面表示用のラベル。</param>
public sealed record SyncModeOption(SyncMode Mode, string Label)
{
    /// <summary>コンボボックス表示用に<see cref="Label" />を返す。</summary>
    public override string ToString()
    {
        return Label;
    }
}

/// <summary>差分一覧の絞り込みフィルター1項目(該当件数を保持し、変更を通知する)。</summary>
/// <param name="Label">表示ラベル。</param>
/// <param name="Kind">絞り込み対象の変更種別(nullの場合は全件)。</param>
/// <param name="ChangesOnly">追加・削除・エラーのみに絞り込むかどうか。</param>
public sealed record ChangeFilter(string Label, ChangeKind? Kind, bool ChangesOnly = false) : INotifyPropertyChanged
{
    // ComboBoxはSelectedItemの表示にToString()の値をキャプチャして使うため、Countを更新して
    // CollectionViewSource.Refresh()を呼ぶだけではドロップダウン内のリストは更新されても、
    // 選択中アイテムの表示テキストだけが古いままになる。DisplayTextへINotifyPropertyChangedで
    // 変更通知することで、選択中の表示も含めて確実に更新されるようにする。
    /// <summary>このフィルターに該当する件数(未確認の場合は-1)。</summary>
    public int Count
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
        }
    } = -1;

    /// <summary>件数を含めたコンボボックス表示用テキスト(未確認の場合は件数なし)。</summary>
    public string DisplayText => Count >= 0 ? $"{Label} ({Count})" : Label;

    // recordの既定Equals/GetHashCodeはCountも比較対象に含めてしまう。UpdateFilterCountsで
    // 件数を更新するたびにハッシュ値が変わり、WPFのComboBoxが選択中の項目を見失う不具合になったため、
    // 識別に使う3項目だけで明示的に判定する。
    /// <summary>Countを除く3項目(Label・Kind・ChangesOnly)のみで等価性を判定する。</summary>
    public bool Equals(ChangeFilter? other)
    {
        return other is not null && Label == other.Label && Kind == other.Kind && ChangesOnly == other.ChangesOnly;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Countを除く3項目(Label・Kind・ChangesOnly)から算出するハッシュ値。</summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Label, Kind, ChangesOnly);
    }

    /// <summary>「変更あり」絞り込み(<see cref="ChangesOnly" />)に該当する種別かどうかを判定する。</summary>
    public static bool MatchesChangesOnly(ChangeKind kind)
    {
        return kind is ChangeKind.Add or ChangeKind.Remove or ChangeKind.Error;
    }
}

/// <summary>差分一覧DataGridの1行分の表示用ラップ。</summary>
/// <param name="Change">元となる変更内容。</param>
public sealed record SyncChangeRowViewModel(SyncChange Change)
{
    /// <summary>変更の種別。</summary>
    public ChangeKind Kind => Change.Kind;

    /// <summary>種別の日本語表示ラベル。</summary>
    public string KindLabel => Kind switch
    {
        ChangeKind.Add => "追加", ChangeKind.Remove => "削除", ChangeKind.Keep => "維持",
        ChangeKind.Protected => "所有者保護", ChangeKind.NotMember => "未所属",
        ChangeKind.Error => "エラー", ChangeKind.Excluded => "個別除外", _ => Kind.ToString()
    };

    /// <summary>対象ユーザーの表示名。</summary>
    public string DisplayName => Change.DisplayName;

    /// <summary>入力アドレス(またはメールアドレス)。</summary>
    public string Email => Change.Email;

    /// <summary>業務上の理由を日本語へ変換した詳細説明。</summary>
    public string Detail => SyncChangeReasonText.Format(Change.Reason);
}

/// <summary>同期差分の理由コードを画面表示用の日本語へ変換する。</summary>
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

// 実行結果のうち失敗した操作だけを画面に残すための行。原因(Error)と対象をSnackbarが消えた後も確認できるようにする。
/// <summary>同期実行結果1件分の表示用ラップ(主に失敗した操作の一覧表示に使用)。</summary>
/// <param name="Result">元となる実行結果。</param>
public sealed record SyncResultRowViewModel(SyncOperationResult Result)
{
    /// <summary>種別の日本語表示ラベル。</summary>
    public string KindLabel => Result.Kind == ChangeKind.Add ? "追加" : "削除";

    /// <summary>対象のアドレス。</summary>
    public string Email => Result.Email;

    /// <summary>失敗時のエラーメッセージ。</summary>
    public string Error => Result.Error ?? "";
}