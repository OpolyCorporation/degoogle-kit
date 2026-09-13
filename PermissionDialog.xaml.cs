using System.Windows;
using DeGoogleKit.Services;

namespace DeGoogleKit;

public partial class PermissionDialog : Window
{
    public bool Remember => RememberBox.IsChecked == true;

    public PermissionDialog(PermissionService.PermissionSpec spec)
    {
        InitializeComponent();
        ThemeChrome.Apply(this);
        Title = spec.Title;
        TitleText.Text = spec.Title;
        SummaryText.Text = spec.Summary;
        AccessText.Text = spec.Access;
        ChangeText.Text = string.IsNullOrWhiteSpace(spec.Change) ? "Nothing." : spec.Change;
        ChangeBadge.Visibility = spec.ChangesSystem ? Visibility.Visible : Visibility.Collapsed;
        RememberBox.Visibility = spec.Rememberable ? Visibility.Visible : Visibility.Collapsed;
        RememberBox.IsChecked = spec.Rememberable && string.IsNullOrWhiteSpace(spec.AllowLabel);
        AllowBtn.Content = !string.IsNullOrWhiteSpace(spec.AllowLabel)
            ? spec.AllowLabel
            : spec.ChangesSystem ? "Allow this change" : "Allow";
    }

    private void OnAllow(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnDeny(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
