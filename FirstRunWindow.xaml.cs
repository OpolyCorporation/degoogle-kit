using System.Windows;
using DeGoogleKit.Services;

namespace DeGoogleKit;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
        ThemeChrome.Apply(this);
        NoticeBox.Text = LegalCopy.PrivacyNotice;
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
                "Please tick the age, local storage, and privacy notice boxes. GDPR requires consent to be specific, informed, and freely given — we cannot pre-tick them for you.",
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
        PermissionService.Set(AccessKind.AccountCloud, SupabaseBox.IsChecked == true);
        if (DesktopShortcutBox.IsChecked == true)
            ShortcutService.CreateDesktop();
        if (StartMenuShortcutBox.IsChecked == true)
            ShortcutService.CreateStartMenu();
        DialogResult = true;
        Close();
    }
}
