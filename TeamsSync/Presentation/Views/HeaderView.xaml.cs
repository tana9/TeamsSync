namespace TeamsSync.Presentation.Views;

/// <summary>アカウント表示・サインイン/サインアウトボタンを含むヘッダー部分のView。</summary>
public partial class HeaderView
{
    /// <summary>コンストラクター。表示時にサインインボタンへ初期フォーカスする。</summary>
    public HeaderView()
    {
        InitializeComponent();
        Loaded += (_, _) => SignInButton.Focus();
    }
}