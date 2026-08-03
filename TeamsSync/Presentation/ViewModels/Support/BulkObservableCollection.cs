using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace TeamsSync.Presentation.ViewModels.Support;

/// <summary>複数項目を1回のReset通知で差し替えられるObservableCollection</summary>
internal sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>現在の全項目を置き換え、バインディング先へ変更を1回だけ通知する</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}