using Microsoft.Win32;

namespace TeamsSync.Presentation.Services;

/// <summary>Win32の標準ファイルダイアログで入出力ファイルを選択させる。</summary>
public sealed class WpfFilePickerService : IFilePickerService
{
    public string? PickMemberFile(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "メンバーリストを選択",
            Filter = "メンバーリスト (*.csv;*.xlsx)|*.csv;*.xlsx|CSV (*.csv)|*.csv|Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
