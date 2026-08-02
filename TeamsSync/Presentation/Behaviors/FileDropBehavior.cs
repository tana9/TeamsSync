using System.Windows;
using System.Windows.Input;

namespace TeamsSync.Presentation.Behaviors;

/// <summary>
///     任意のWPF要素にファイルドロップを受け付けさせ、ドロップされたファイルパスを
///     アタッチされたコマンドへ渡す添付ビヘイビア
/// </summary>
public static class FileDropBehavior
{
    /// <summary>ドロップされたファイルパスを実行するコマンドを指定する添付プロパティ</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(FileDropBehavior), new PropertyMetadata(null, OnCommandChanged));

    /// <summary><see cref="CommandProperty" />の値を設定する</summary>
    public static void SetCommand(DependencyObject element, ICommand value)
    {
        element.SetValue(CommandProperty, value);
    }

    /// <summary><see cref="CommandProperty" />の値を取得する</summary>
    public static ICommand? GetCommand(DependencyObject element)
    {
        return (ICommand?)element.GetValue(CommandProperty);
    }

    /// <summary>コマンドの設定・解除に合わせて、要素のドロップ受付とイベント購読を切り替える</summary>
    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.AllowDrop = e.NewValue is not null;
        element.Drop -= OnDrop;
        if (e.NewValue is not null)
        {
            element.Drop += OnDrop;
        }
    }

    /// <summary>ドロップされた最初のファイルパスをアタッチされたコマンドへ渡して実行する</summary>
    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject element || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
        string? path = files?.FirstOrDefault();
        ICommand? command = GetCommand(element);
        if (path is not null && command?.CanExecute(path) == true)
        {
            command.Execute(path);
        }
    }
}