using System.Windows;
using DeGoogleKit.Services;

namespace DeGoogleKit;

public partial class UpdateDialog : Window
{
    private readonly UpdateManifest _manifest;
    private bool _busy;

    public UpdateDialog(UpdateManifest manifest)
    {
        InitializeComponent();
        ThemeChrome.Apply(this);
        _manifest = manifest;
        VersionText.Text = $"v{AppInfo.VersionText}  →  v{manifest.Version}";
        ChangelogText.Text = string.IsNullOrWhiteSpace(manifest.Changelog)
            ? "A newer DeGoogle Kit is ready. The download is signed with a checksum and comes from the HTTPS feed in Privacy — never Google."
            : manifest.Changelog;
        var when = DateTime.Today.AddDays(1);
        MidnightBtn.Content = $"At midnight ({when:ddd} 00:00) — install while I sleep";
    }

    private async void OnNow(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunAsync("Downloading update…", async progress =>
        {
            var zip = await UpdateService.DownloadAsync(_manifest, progress);
            StatusText.Text = "Installing and restarting…";
            UpdateService.ApplyAndRestart(_manifest, zip);
        });
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        UpdateService.ScheduleLater(_manifest);
        DialogResult = true;
        Close();
    }

    private async void OnMidnight(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunAsync("Downloading now. It will install at midnight.", async progress =>
        {
            string? zip = null;
            try
            {
                zip = await UpdateService.DownloadAsync(_manifest, progress);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Could not download yet. We’ll retry at midnight.\n" + ex.Message;
            }

            UpdateService.ScheduleMidnight(_manifest, zip);
            MessageBox.Show(
                zip is null
                    ? $"Install is scheduled for {DateTime.Today.AddDays(1):dddd} at 00:00. We’ll download then, as long as this PC is on."
                    : $"The package is ready. DeGoogle Kit will install {_manifest.Version} at 00:00 if the app is open, or the next time you open it after midnight.",
                "Scheduled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
            Close();
        });
    }

    private async Task RunAsync(string status, Func<IProgress<double>, Task> work)
    {
        _busy = true;
        NowBtn.IsEnabled = LaterBtn.IsEnabled = MidnightBtn.IsEnabled = false;
        DownloadBar.Visibility = Visibility.Visible;
        DownloadBar.Value = 0;
        StatusText.Text = status;
        var progress = new Progress<double>(v => DownloadBar.Value = v);
        try
        {
            await work(progress);
        }
        catch (Exception ex)
        {
            _busy = false;
            NowBtn.IsEnabled = LaterBtn.IsEnabled = MidnightBtn.IsEnabled = true;
            DownloadBar.Visibility = Visibility.Collapsed;
            MessageBox.Show(ex.Message, "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
