using CommunityToolkit.Mvvm.ComponentModel;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>ファイル入力の読込中状態と表示状態を保持する</summary>
public sealed partial class MemberFileLoadState : ObservableObject
{
    [ObservableProperty]
    public partial string InfoText { get; set; } = "ファイルを選択するか、ここへドロップしてください";

    [ObservableProperty]
    public partial string Path { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }
}

/// <summary>貼り付け入力の解析状態と表示状態を保持する</summary>
public sealed partial class MemberPasteInputState : ObservableObject
{
    [ObservableProperty]
    public partial string InfoText { get; set; } = "1行につき1ユーザー（氏名またはメールアドレス）";

    [ObservableProperty]
    public partial bool IsParsing { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }
}