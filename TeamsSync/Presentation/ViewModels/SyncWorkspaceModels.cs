using System.ComponentModel;
using TeamsSync.Domain.Teams;

namespace TeamsSync.Presentation.ViewModels;

// SyncWorkspaceViewModelが扱う画面表示用のモデル型。ロジックを持たないデータ表現のため、
// 同期実行ロジック本体(SyncWorkspaceViewModel.cs)とは別ファイルに分離している。

public sealed record SyncModeOption(SyncMode Mode, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

public sealed record ChangeFilter(string Label, ChangeKind? Kind, bool ChangesOnly = false) : INotifyPropertyChanged
{
    private int _count = -1;

    // ComboBoxはSelectedItemの表示にToString()の値をキャプチャして使うため、Countを更新して
    // CollectionViewSource.Refresh()を呼ぶだけではドロップダウン内のリストは更新されても、
    // 選択中アイテムの表示テキストだけが古いままになる。DisplayTextへINotifyPropertyChangedで
    // 変更通知することで、選択中の表示も含めて確実に更新されるようにする。
    public int Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
        }
    }

    public string DisplayText => Count >= 0 ? $"{Label} ({Count})" : Label;

    public event PropertyChangedEventHandler? PropertyChanged;

    // recordの既定Equals/GetHashCodeはCountも比較対象に含めてしまう。UpdateFilterCountsで
    // 件数を更新するたびにハッシュ値が変わり、WPFのComboBoxが選択中の項目を見失う不具合になったため、
    // 識別に使う3項目だけで明示的に判定する。
    public bool Equals(ChangeFilter? other) =>
        other is not null && Label == other.Label && Kind == other.Kind && ChangesOnly == other.ChangesOnly;

    public override int GetHashCode() => HashCode.Combine(Label, Kind, ChangesOnly);
}

public sealed record SyncChangeRowViewModel(SyncChange Change)
{
    public ChangeKind Kind => Change.Kind;

    public string KindLabel => Kind switch
    {
        ChangeKind.Add => "追加", ChangeKind.Remove => "削除", ChangeKind.Keep => "変更なし",
        ChangeKind.Protected => "所有者保護", ChangeKind.Error => "エラー", _ => Kind.ToString()
    };

    public string DisplayName => Change.DisplayName;
    public string Email => Change.Email;
    public string Detail => Change.Detail;
}

// 実行結果のうち失敗した操作だけを画面に残すための行。原因(Error)と対象をSnackbarが消えた後も確認できるようにする。
public sealed record SyncResultRowViewModel(SyncOperationResult Result)
{
    public string KindLabel => Result.Kind == ChangeKind.Add ? "追加" : "削除";
    public string Email => Result.Email;
    public string Error => Result.Error ?? "";
}
