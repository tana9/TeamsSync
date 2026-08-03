using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>利用者向けマニュアルを開く操作を管理する</summary>
/// <remarks>コンストラクター</remarks>
public partial class ManualViewModel(IManualService manual, INotificationService dialogs) : ObservableObject
{
    /// <summary>利用者向けマニュアルを開く</summary>
    [RelayCommand]
    private async Task OpenManualAsync()
    {
        try
        {
            manual.OpenManual();
        }
        catch (Exception ex)
        {
            await dialogs.ShowErrorAsync(ex.Message, "マニュアルを開けませんでした");
        }
    }
}