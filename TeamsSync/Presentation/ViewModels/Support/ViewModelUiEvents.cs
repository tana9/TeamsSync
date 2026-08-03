namespace TeamsSync.Presentation.ViewModels.Support;

/// <summary>ViewModelからViewへ渡す状態通知とフォーカス要求を集約する。</summary>
internal sealed class ViewModelUiEvents
{
    public event Action<string, bool>? StatusChanged;
    public event Action? FocusRequested;

    public void Status(string message, bool isError) => StatusChanged?.Invoke(message, isError);
    public void RequestFocus() => FocusRequested?.Invoke();
}
