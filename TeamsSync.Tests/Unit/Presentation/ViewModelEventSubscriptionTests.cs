using TeamsSync.Presentation.Views;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class ViewModelEventSubscriptionTests
{
    [Fact]
    public void 再ロードすると同じViewModelを再購読する()
    {
        object viewModel = new();
        int subscriptions = 0;
        int unsubscriptions = 0;
        ViewModelEventSubscription<object> subscription = new(
            _ => subscriptions++, _ => unsubscriptions++);

        subscription.Load(viewModel);
        subscription.Unload();
        subscription.Load(viewModel);

        Assert.Equal(2, subscriptions);
        Assert.Equal(1, unsubscriptions);
    }

    [Fact]
    public void ロード中のDataContext変更は旧ViewModelを解除して新ViewModelだけを購読する()
    {
        object first = new();
        object second = new();
        List<object> subscribed = [];
        List<object> unsubscribed = [];
        ViewModelEventSubscription<object> subscription = new(
            subscribed.Add, unsubscribed.Add);

        subscription.Load(first);
        subscription.ChangeDataContext(second);

        Assert.Equal([first, second], subscribed);
        Assert.Equal([first], unsubscribed);
    }

    [Fact]
    public void 同じ状態の通知が連続しても二重購読しない()
    {
        object viewModel = new();
        int subscriptions = 0;
        int unsubscriptions = 0;
        ViewModelEventSubscription<object> subscription = new(
            _ => subscriptions++, _ => unsubscriptions++);

        subscription.ChangeDataContext(viewModel);
        subscription.Load(viewModel);
        subscription.Load(viewModel);
        subscription.ChangeDataContext(viewModel);
        subscription.Unload();
        subscription.Unload();

        Assert.Equal(1, subscriptions);
        Assert.Equal(1, unsubscriptions);
    }

    [Fact]
    public void アンロード中のDataContext変更は次のロードまで購読しない()
    {
        object first = new();
        object second = new();
        List<object> subscribed = [];
        ViewModelEventSubscription<object> subscription = new(subscribed.Add, _ => { });

        subscription.Load(first);
        subscription.Unload();
        subscription.ChangeDataContext(second);

        Assert.Equal([first], subscribed);

        subscription.Load(second);
        Assert.Equal([first, second], subscribed);
    }
}
