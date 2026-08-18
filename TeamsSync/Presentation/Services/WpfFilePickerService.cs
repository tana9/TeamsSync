using Microsoft.Win32;

namespace TeamsSync.Presentation.Services;

/// <summary>Win32の標準ファイルダイアログで入出力ファイルを選択させる</summary>
public sealed class WpfFilePickerService : IFilePickerService
{
    /// <inheritdoc />
    public string? PickMemberFile(string? initialDirectory)
    {
        OpenFileDialog dialog = new()
        {
            Title = "メンバーリストを選択",
            Filter = "メンバーリスト (*.csv;*.xlsx)|*.csv;*.xlsx|CSV (*.csv)|*.csv|Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickSaveLocation(string suggestedFileName, string? initialDirectory)
    {
        SaveFileDialog dialog = new()
        {
            Title = "CSVファイルの保存先を選択",
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = suggestedFileName,
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : null
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}