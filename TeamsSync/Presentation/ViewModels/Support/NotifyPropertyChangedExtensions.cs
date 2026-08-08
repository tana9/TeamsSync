using System.ComponentModel;

namespace TeamsSync.Presentation.ViewModels.Support;

// MainWindowViewModel・WorkflowStepsViewModelなど、複数のViewModelが
// 「PropertyChanged += (_, e) => { if (e.PropertyName == nameof(X)) { ... } }」という
// 同じ形の定型コードを、監視先・プロパティ名を変えて何度も書いていた。switch文で複数プロパティを
// まとめて分岐しているケースもあり、どの購読がどの反応に対応するか読み取りにくかった。
// この定型コードを1箇所へ集約し、呼び出し側では「どのプロパティの変化に何を反応させるか」だけが
// 1行で見えるようにする
/// <summary><see cref="INotifyPropertyChanged" />の特定プロパティの変化だけを購読するための拡張メソッド</summary>
internal static class NotifyPropertyChangedExtensions
{
    /// <summary><paramref name="propertyName" />が変化したときだけ<paramref name="callback" />を呼び出す購読を登録する</summary>
    public static void WhenChanged(this INotifyPropertyChanged source, string propertyName, Action callback)
    {
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName)
            {
                callback();
            }
        };
    }
}
