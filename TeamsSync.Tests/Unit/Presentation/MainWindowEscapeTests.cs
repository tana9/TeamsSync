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

    [Theory]
    [InlineData(false, false, false, (int)CloseAction.AllowClose)]
    [InlineData(false, false, true, (int)CloseAction.StartCancelAndWait)]
    // 待機中(cancellingBeforeClose=true)に再度閉じる操作をされても、
    // キャンセル待機やCloseの二重呼び出しにつながらないよう保留する
    [InlineData(false, true, true, (int)CloseAction.BlockWhilePendingCancellation)]
    [InlineData(false, true, false, (int)CloseAction.BlockWhilePendingCancellation)]
    // キャンセル完了後、自身のClose()が再発火させたClosingイベントは素通りさせる
    [InlineData(true, true, true, (int)CloseAction.AllowClose)]
    [InlineData(true, false, false, (int)CloseAction.AllowClose)]
    public void 閉じる操作は待機中の再クローズを二重処理しない(bool closeAfterCancellation, bool cancellingBeforeClose,
        bool syncing, int expected)
    {
        Assert.Equal((CloseAction)expected,
            MainWindow.DecideCloseAction(closeAfterCancellation, cancellingBeforeClose, syncing));
    }
}