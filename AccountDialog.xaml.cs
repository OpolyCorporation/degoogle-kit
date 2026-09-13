using System.Diagnostics;
using System.Windows;
using DeGoogleKit.Services;

namespace DeGoogleKit;

public partial class AccountDialog : Window
{
    public bool SignedIn { get; private set; }

    public AccountDialog()
    {
        InitializeComponent();
        ThemeChrome.Apply(this);
        if (AccountService.IsConfigured)
        {
            ProjectPanel.Visibility = Visibility.Collapsed;
            UrlBox.Text = AccountService.Url;
        }
        else
        {
            StatusText.Text = "Paste your free-plan Supabase URL and anon key once (Authentication → API). Then create an account.";
        }
        var session = AccountService.Session;
        if (session is not null)
            EmailBox.Text = session.Email;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private async void OnSignUp(object sender, RoutedEventArgs e) => await RunAsync(signUp: true);

    private async void OnSignIn(object sender, RoutedEventArgs e) => await RunAsync(signUp: false);

    private void OnOpenPrivacy(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(LegalCopy.PrivacyUrl) { UseShellExecute = true });
        }
        catch
        {
            StatusText.Text = LegalCopy.PrivacyUrl;
        }
    }

    private async Task RunAsync(bool signUp)
    {
        if (signUp && AccountConsentBox.IsChecked != true)
        {
            StatusText.Text = "Tick the account processing box to create an account (GDPR Art. 7). Signing in to an existing account does not need that extra tick.";
            return;
        }
        if (!AccountService.IsConfigured)
        {
            var url = UrlBox.Text.Trim();
            var anon = AnonBox.Text.Trim();
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || anon.Length < 20)
            {
                StatusText.Text = "Enter the https://….supabase.co URL and the anon public key from the Supabase dashboard.";
                return;
            }
            AccountService.SaveProject(url, anon);
        }

        IsEnabled = false;
        try
        {
            var result = signUp
                ? await AccountService.SignUpAsync(EmailBox.Text, PasswordBox.Password)
                : await AccountService.SignInAsync(EmailBox.Text, PasswordBox.Password);
            StatusText.Text = result.Message;
            if (!result.Ok) return;
            SignedIn = AccountService.IsSignedIn;
            if (SignedIn)
            {
                DialogResult = true;
                Close();
            }
        }
        finally
        {
            IsEnabled = true;
        }
    }
}
