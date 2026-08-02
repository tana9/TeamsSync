using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TeamsSync.Presentation.Services;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>利用者向けマニュアルを開く操作を管理する</summary>
public partial class ManualViewModel : ObservableObject
{
    private readonly INotificationService _dialogs;
    private readonly IManualService _manual;

    /// <summary>コンストラクター</summary>
    public ManualViewModel(IManualService manual, INotificationService dialogs)
    {
        _manual = manual;
        _dialogs = dialogs;
    }

    /// <summary>利用者向けマニュアルを開く</summary>
    [RelayCommand]
    private async Task OpenManualAsync()
    {
        try
        {
            _manual.OpenManual();
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(ex.Message, "マニュアルを開けませんでした");
        }
    }
}