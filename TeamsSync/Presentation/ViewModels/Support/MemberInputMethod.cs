namespace TeamsSync.Presentation.ViewModels.Support;

// MemberFileViewModel.SelectedInputIndexはTabControl.SelectedIndexへ直接バインドするためint型のまま
// 公開しているが、0/1の生リテラル比較がMemberFileViewModel・MemberInputDocumentState・
// MemberInputSelectionCoordinatorの複数箇所に散らばっていた。比較箇所では(int)MemberInputMethod.Xxxを
// 使うことで、タイプミスをコンパイルエラーとして検出できるようにする
/// <summary>メンバーリストの入力方法(<see cref="TeamsSync.Presentation.ViewModels.MemberFileViewModel.SelectedInputIndex" />が表す0/1に対応)</summary>
internal enum MemberInputMethod
{
    /// <summary>ファイル選択</summary>
    File = 0,

    /// <summary>テキスト貼り付け</summary>
    Paste = 1
}
