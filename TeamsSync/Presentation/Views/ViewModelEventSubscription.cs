namespace TeamsSync.Presentation.Views;

/// <summary>
///     Viewのロード状態とDataContext変更に合わせて、ViewModelイベントの購読先を1つだけ維持する。
/// </summary>
internal sealed class ViewModelEventSubscription<TViewModel>(
    Action<TViewModel> subscribe,
    Action<TViewModel> unsubscribe)
    where TViewModel : class
{
    private TViewModel? _dataContext;
    private bool _isLoaded;
    private bool _isSubscribed;

    /// <summary>Viewのロード時に現在のDataContextを記録し、イベントを購読する。</summary>
    public void Load(TViewModel? dataContext)
    {
        _isLoaded = true;
        ChangeDataContext(dataContext);
        SubscribeCurrent();
    }

    /// <summary>Viewのアンロード時にイベント購読を解除する。</summary>
    public void Unload()
    {
        _isLoaded = false;
        UnsubscribeCurrent();
    }

    /// <summary>DataContext変更時に旧ViewModelを解除し、ロード中なら新ViewModelを購読する。</summary>
    public void ChangeDataContext(TViewModel? dataContext)
    {
        if (ReferenceEquals(_dataContext, dataContext))
        {
            return;
        }

        UnsubscribeCurrent();
        _dataContext = dataContext;
        SubscribeCurrent();
    }

    private void SubscribeCurrent()
    {
        if (!_isLoaded || _isSubscribed || _dataContext is null)
        {
            return;
        }

        subscribe(_dataContext);
        _isSubscribed = true;
    }

    private void UnsubscribeCurrent()
    {
        if (!_isSubscribed || _dataContext is null)
        {
            return;
        }

        unsubscribe(_dataContext);
        _isSubscribed = false;
    }
}