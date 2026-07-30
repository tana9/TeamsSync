using System.Windows;
using System.Windows.Input;

namespace TeamsSync.Presentation.Behaviors;

public static class FileDropBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(FileDropBehavior), new PropertyMetadata(null, OnCommandChanged));

    public static void SetCommand(DependencyObject element, ICommand value)
    {
        element.SetValue(CommandProperty, value);
    }

    public static ICommand? GetCommand(DependencyObject element)
    {
        return (ICommand?)element.GetValue(CommandProperty);
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.AllowDrop = e.NewValue is not null;
        element.Drop -= OnDrop;
        if (e.NewValue is not null) element.Drop += OnDrop;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject element || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        var path = files?.FirstOrDefault();
        var command = GetCommand(element);
        if (path is not null && command?.CanExecute(path) == true) command.Execute(path);
    }
}