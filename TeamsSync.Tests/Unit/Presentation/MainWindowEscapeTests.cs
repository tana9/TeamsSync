using TeamsSync.Presentation.Views;

namespace TeamsSync.Tests.Unit.Presentation;

public sealed class MainWindowEscapeTests
{
    [Theory]
    [InlineData(true, true, true, (int)EscapeAction.DeferToDialog)]
    [InlineData(false, true, true, (int)EscapeAction.DeferToSyncCancellation)]
    [InlineData(false, false, true, (int)EscapeAction.DismissSnackbar)]
    [InlineData(false, false, false, (int)EscapeAction.Unhandled)]
    public void ESCは最前面の一時UIを優先する(bool dialog, bool syncing, bool snackbar,
        int expected)
    {
        Assert.Equal((EscapeAction)expected, MainWindow.DecideEscapeAction(dialog, syncing, snackbar));
    }
}
