using TeamsSync.Application.Models;
using TeamsSync.Presentation.ViewModels;

namespace TeamsSync.Presentation.ViewModels.Support;

/// <summary>入力タブ切替と貼り付け文書の無効化に伴う状態遷移を調整する。</summary>
internal sealed class MemberInputSelectionCoordinator(MemberInputDocumentState documents)
{
    public MemberListDocument? Select(int index, string pastedText, out string? pasteInfo)
    {
        documents.SelectInput(index);
        // 貼り付けタブに切り替えたとき、テキストが編集されていなければ以前「入力を反映」済みの
        // PastedDocumentをそのまま維持する(テキストが変わった場合はOnPastedTextChangedが既に
        // PastedDocumentをnullへ戻しているので、ここで再び「反映してください」の案内に戻る)。
        // ファイルタブと同様にActiveDocumentへ寄せることで、タブを往復するたびに反映済みの
        // 入力が失われる不具合を防ぐ
        if (index == (int)MemberInputMethod.Paste && documents.PastedDocument is null)
        {
            pasteInfo = string.IsNullOrWhiteSpace(pastedText)
                ? MemberPasteInputState.DefaultInfoText
                : "「入力を反映」を押してください";
            return null;
        }

        pasteInfo = null;
        return documents.ActiveDocument;
    }

    public void InvalidatePastedDocument()
    {
        documents.SetPastedDocument(null);
    }
}