using CommunityToolkit.Mvvm.ComponentModel;

using TeamsSync.Application.Models;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>ファイル文書と貼り付け文書、および選択中の入力文書を管理する</summary>
public sealed class MemberInputDocumentState
{
    /// <summary>ファイルから読み込んだ文書</summary>
    public MemberListDocument? FileDocument { get; private set; }

    /// <summary>貼り付け入力から解析した文書</summary>
    public MemberListDocument? PastedDocument { get; private set; }

    /// <summary>選択中の入力方法(0=ファイル、1=テキスト貼り付け)</summary>
    public int SelectedInputIndex { get; private set; }

    /// <summary>現在選択中の文書</summary>
    public MemberListDocument? ActiveDocument => SelectedInputIndex == 0 ? FileDocument : PastedDocument;

    /// <summary>ファイル文書を更新する</summary>
    public void SetFileDocument(MemberListDocument? document)
    {
        FileDocument = document;
    }

    /// <summary>貼り付け文書を更新する</summary>
    public void SetPastedDocument(MemberListDocument? document)
    {
        PastedDocument = document;
    }

    /// <summary>入力方法を切り替える</summary>
    public void SelectInput(int index)
    {
        if (index is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "入力方法はファイル(0)または貼り付け(1)です。");
        }

        SelectedInputIndex = index;
    }
}

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
