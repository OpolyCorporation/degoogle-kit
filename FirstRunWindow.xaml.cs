using System.Windows;
using DeGoogleKit.Services;

namespace DeGoogleKit;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
        ThemeChrome.Apply(this);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnAgree(object sender, RoutedEventArgs e)
    {
        if (AgeBox.IsChecked != true || LocalBox.IsChecked != true || PrivacyBox.IsChecked != true)
        {
            MessageBox.Show(
                "Please tick all three boxes. GDPR requires consent to be specific, informed, and freely given — we cannot pre-tick them for you.",
                "Consent needed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        PrivacyStore.SaveConsent(new ConsentRecord
        {
            AgeConfirmed = true,
            LocalStorageAccepted = true,
            PrivacyAccepted = true,
            CloudAiConsent = false
        });
        PermissionService.Set(AccessKind.PcScan, ScanBox.IsChecked == true);
        if (DesktopShortcutBox.IsChecked == true)
            ShortcutService.CreateDesktop();
        if (StartMenuShortcutBox.IsChecked == true)
            ShortcutService.CreateStartMenu();
        DialogResult = true;
        Close();
    }
}
