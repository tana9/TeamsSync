using TeamsSync.Application.Models;

namespace TeamsSync.Presentation.ViewModels;

/// <summary>入力タブ切替と貼り付け文書の無効化に伴う状態遷移を調整する。</summary>
internal sealed class MemberInputSelectionCoordinator(MemberInputDocumentState documents)
{
    public MemberListDocument? Select(int index, string pastedText, out string? pasteInfo)
    {
        documents.SelectInput(index);
        if (index == 0)
        {
            pasteInfo = null;
            return documents.FileDocument;
        }

        pasteInfo = string.IsNullOrWhiteSpace(pastedText)
            ? "1行につき1ユーザー（氏名またはメールアドレス）"
            : "「入力を反映」を押してください";
        return null;
    }

    public void InvalidatePastedDocument()
    {
        documents.SetPastedDocument(null);
    }
}
