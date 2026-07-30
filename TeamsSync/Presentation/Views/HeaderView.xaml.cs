namespace TeamsSync.Presentation.Views;

public partial class HeaderView
{
    public HeaderView()
    {
        InitializeComponent();
        Loaded += (_, _) => SignInButton.Focus();
    }
}
