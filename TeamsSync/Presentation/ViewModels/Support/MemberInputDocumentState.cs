using TeamsSync.Application.Models;

namespace TeamsSync.Presentation.ViewModels.Support;

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
    public MemberListDocument? ActiveDocument =>
        SelectedInputIndex == (int)MemberInputMethod.File ? FileDocument : PastedDocument;

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
        if (index is not ((int)MemberInputMethod.File or (int)MemberInputMethod.Paste))
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "入力方法はファイル(0)または貼り付け(1)です。");
        }

        SelectedInputIndex = index;
    }
}
