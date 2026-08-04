using TeamsSync.Application.Models;
using TeamsSync.Presentation.ViewModels;

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

    /// <summary>
    ///     入力方法を切り替え、切替後の有効な文書を返す。貼り付けタブへの切替時、まだ「入力を反映」
    ///     していない場合は文書の代わりに案内文を<paramref name="pasteInfo" />で返す
    /// </summary>
    public MemberListDocument? Select(int index, string pastedText, out string? pasteInfo)
    {
        SelectInput(index);
        // 貼り付けタブに切り替えたとき、テキストが編集されていなければ以前「入力を反映」済みの
        // PastedDocumentをそのまま維持する(テキストが変わった場合はOnPastedTextChangedが既に
        // PastedDocumentをnullへ戻しているので、ここで再び「反映してください」の案内に戻る)。
        // ファイルタブと同様にActiveDocumentへ寄せることで、タブを往復するたびに反映済みの
        // 入力が失われる不具合を防ぐ
        if (index == (int)MemberInputMethod.Paste && PastedDocument is null)
        {
            pasteInfo = string.IsNullOrWhiteSpace(pastedText)
                ? MemberPasteInputState.DefaultInfoText
                : "「入力を反映」を押してください";
            return null;
        }

        pasteInfo = null;
        return ActiveDocument;
    }

    /// <summary>貼り付け文書を無効化(未反映状態に)する</summary>
    public void InvalidatePastedDocument()
    {
        SetPastedDocument(null);
    }
}