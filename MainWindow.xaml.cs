using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeGoogleKit.Models;
using DeGoogleKit.Services;
using Microsoft.Win32;

namespace DeGoogleKit;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DetectedApp> _apps = [];
    private readonly ObservableCollection<ChatMessage> _chat = [];
    private readonly ObservableCollection<TakeoutEntry> _takeout = [];
    private readonly List<GuideItem> _guide;
    private PlanState _planState = PlanStore.Load();
    private List<PlanRow> _planRows = [];
    private TakeoutInventory? _inventory;
    private ScanSnapshot _scan = new();
    private bool _uiReady;
    private bool _applyingUpdate;
    private bool _askedMidnight;
    private bool _permUiReady;
    private FrameworkElement? _lastPage;
    private int _lastScore = -1;
    private bool _dropLit;
    private readonly DispatcherTimer _updateClock = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _keepAliveClock = new() { Interval = TimeSpan.FromHours(24) };
    private readonly DispatcherTimer _cloudSyncTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _cloudApplying;

    private FileSystemWatcher? _licenseWatch;
    private int _licenseRedeemBusy;

    public MainWindow()
    {
        InitializeComponent();
        ThemeChrome.Apply(this);
        _guide = ChecklistStore.Load();
        foreach (var item in _guide)
            item.PropertyChanged += OnGuideItemChanged;

        GuideList.ItemsSource = _guide;
        AppList.ItemsSource = _apps;
        ChatList.ItemsSource = _chat;
        TakeoutList.ItemsSource = _takeout;
        RebuildPlan();
        ApplyModeRadios();
        GdprList.ItemsSource = GdprCatalog.Rights;
        DataMapText.Text = PrivacyStore.DataMap();
        LegalNoticeBox.Text = LegalCopy.PrivacyNotice;
        CloudAiConsentBox.IsChecked = PrivacyStore.Consent.CloudAiConsent;
        FillAiProviders();
        SelectProvider(PrivacyStore.Settings.AiProvider);
        MigrateRetiredGroqModel();
        AiModelBox.Text = PrivacyStore.Settings.AiModel;
        AiBaseUrlBox.Text = PrivacyStore.Settings.AiBaseUrl;
        ApplyAiProviderUi();
        UpdateCoachConnectUi();
        ApplyHostedAiUi();
        RefreshLicenseUi();
        RefreshAudit();
        UpdateGuideStats();
        VersionLabel.Text = "v" + AppInfo.VersionText;
        UpdateFeedBox.Text = string.IsNullOrWhiteSpace(PrivacyStore.Settings.UpdateFeedUrl)
            ? UpdateService.DefaultFeedUrl
            : PrivacyStore.Settings.UpdateFeedUrl;
        UpdateStatusText.Text = "Current version " + AppInfo.VersionText + ". Checking the cloud is optional and never goes to Google.";
        _chat.Add(new ChatMessage
        {
            Role = "Coach",
            Text = "I’m the DeGoogle coach. Ask about Gmail, Photos, Chrome, Takeout, 2FA, phone, or GDPR — I’ll walk you through it.\n\nStart with Offline or Topics (no key). Optional: Get a free Groq key once, paste it, tick consent, then Send. I’m not a general ChatGPT replacement."
        });
        _uiReady = true;
        BindPermissionToggles();
        WatchPendingLicense();
        _keepAliveClock.Tick += async (_, _) =>
        {
            if (AccountService.IsSignedIn)
                await AccountService.TouchKeepaliveAsync();
        };
        _cloudSyncTimer.Tick += async (_, _) =>
        {
            _cloudSyncTimer.Stop();
            await PushCloudProgressAsync();
        };
        _updateClock.Tick += async (_, _) =>
        {
            if (_applyingUpdate) return;
            _applyingUpdate = true;
            try { await TryApplyScheduledUpdateAsync(); }
            finally { _applyingUpdate = false; }
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Motion.Pop(BrandMark);
        Motion.Pop(LicenseBadge);
        _lastPage = PageOverview;
        Motion.EnterPage(PageOverview);
        _updateClock.Start();
        _keepAliveClock.Start();
        RefreshAccountUi();
        if (AccountService.IsSignedIn)
        {
            var alreadyChosen = PermissionService.Get(AccessKind.AccountCloud) is not null;
            if (AskAccess.For(this, AccessKind.AccountCloud,
                    "Allow DeGoogle Kit to use Supabase to restore your plan and checklist? You can refuse and stay local.",
                    automatic: alreadyChosen))
            {
                _ = AccountService.TouchKeepaliveAsync();
                await RestoreAccountProgressAsync(interactive: false);
                await RestoreAccountLicenseAsync();
            }
        }
        await RedeemLaunchLicenseAsync();
        if (await TryApplyScheduledUpdateAsync()) return;
        await RunScanAsync(automatic: true);
        if (UpdateService.ShouldRemindOnLaunch() && UpdateService.State.LastManifest is { } cached)
            OfferUpdate(cached);
        BindPermissionToggles();
    }

    private async Task<bool> TryApplyScheduledUpdateAsync()
    {
        if (!UpdateService.IsScheduledUpdateDue()) return false;
        if (_askedMidnight) return false;
        _askedMidnight = true;
        var manifest = UpdateService.State.LastManifest;
        if (manifest is null) return false;
        if (!AskAccess.For(this, AccessKind.InstallUpdate,
                $"Version {manifest.Version} was scheduled for midnight. Install and restart now?"))
        {
            UpdateService.SnoozeDueUpdate();
            return false;
        }
        return await UpdateService.ApplyDueAsync(this);
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (PageOverview is null || PagePlan is null) return;
        PageOverview.Visibility = Vis(NavOverview);
        PagePlan.Visibility = Vis(NavPlan);
        PagePc.Visibility = Vis(NavPc);
        PageGuide.Visibility = Vis(NavGuide);
        PageCoach.Visibility = Vis(NavCoach);
        PageGdpr.Visibility = Vis(NavGdpr);
        PageNetwork.Visibility = Vis(NavNetwork);
        PageTakeout.Visibility = Vis(NavTakeout);
        PagePro.Visibility = Vis(NavPro);
        PagePrivacy.Visibility = Vis(NavPrivacy);
        if (NavPrivacy.IsChecked == true)
        {
            RefreshAudit();
            BindPermissionToggles();
        }

        if (!_uiReady) return;
        var page = CurrentPage();
        if (page is null || ReferenceEquals(page, _lastPage)) return;
        _lastPage = page;
        Motion.EnterPage(page);
    }

    private FrameworkElement? CurrentPage()
    {
        if (NavOverview.IsChecked == true) return PageOverview;
        if (NavPlan.IsChecked == true) return PagePlan;
        if (NavPc.IsChecked == true) return PagePc;
        if (NavGuide.IsChecked == true) return PageGuide;
        if (NavCoach.IsChecked == true) return PageCoach;
        if (NavGdpr.IsChecked == true) return PageGdpr;
        if (NavNetwork.IsChecked == true) return PageNetwork;
        if (NavTakeout.IsChecked == true) return PageTakeout;
        if (NavPro.IsChecked == true) return PagePro;
        if (NavPrivacy.IsChecked == true) return PagePrivacy;
        return null;
    }

    private void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el)
            Motion.EnterRow(el);
    }

    private static Visibility Vis(RadioButton? nav) =>
        nav?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void OnOpenGuide(object sender, RoutedEventArgs e) => NavGuide.IsChecked = true;
    private void OnOpenTakeout(object sender, RoutedEventArgs e) => NavTakeout.IsChecked = true;
    private void OnOpenPlan(object sender, RoutedEventArgs e) => NavPlan.IsChecked = true;
    private void OnOpenPc(object sender, RoutedEventArgs e) => NavPc.IsChecked = true;
    private void OnOpenCoach(object sender, RoutedEventArgs e) => NavCoach.IsChecked = true;
    private void OnOpenGoogleTakeout(object sender, RoutedEventArgs e) =>
        TryOpenUri("https://takeout.google.com/");
    private void OnTakeoutHintClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OnPickTakeoutZip(sender, e);

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        if (!AskAccess.For(this, AccessKind.UpdateCheck)) return;
        VersionLabel.Text = "v" + AppInfo.VersionText + " · checking…";
        UpdateStatusText.Text = "Contacting the update feed…";
        var result = await UpdateService.CheckAsync();
        VersionLabel.Text = "v" + AppInfo.VersionText;
        UpdateStatusText.Text = result.Message;
        if (!result.ReachedServer || result.Manifest is null)
        {
            MessageBox.Show(result.Message, "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!result.IsNewer)
        {
            MessageBox.Show(result.Message, "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OfferUpdate(result.Manifest);
    }

    private void OnSaveFeed(object sender, RoutedEventArgs e)
    {
        var settings = PrivacyStore.Settings;
        var url = UpdateFeedBox.Text.Trim();
        if (url.Equals(UpdateService.DefaultFeedUrl, StringComparison.OrdinalIgnoreCase))
            url = "";
        if (url.Length > 0 && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("The feed must be HTTPS (or localhost for testing).", "Updates");
            return;
        }
        settings.UpdateFeedUrl = url;
        PrivacyStore.SaveSettings(settings);
        PrivacyStore.Log("update_feed", string.IsNullOrEmpty(url) ? "default" : url);
        MessageBox.Show("Saved. Check for updates will use this URL.", "Updates");
    }

    private void OfferUpdate(UpdateManifest manifest)
    {
        var dialog = new UpdateDialog(manifest) { Owner = this };
        dialog.ShowDialog();
        var state = UpdateService.State;
        if (state.ApplyAt is { } at)
            UpdateStatusText.Text = $"Version {manifest.Version} will install at {at:ddd HH:mm}.";
        else if (state.RemindOnLaunch)
            UpdateStatusText.Text = $"Version {manifest.Version} postponed. We’ll ask again next launch.";
    }

    private async void OnScan(object sender, RoutedEventArgs e) => await RunScanAsync();

    private async Task RunScanAsync(bool automatic = false)
    {
        if (!AskAccess.For(this, AccessKind.PcScan, automatic: automatic))
        {
            ShowScanSkipped();
            return;
        }
        StatApps.Text = "…";
        PcSubtitle.Text = "Scanning installed programs, folders, services, Chrome extensions, DNS, and scheduled tasks…";
        Motion.ScanBusy(ScanRibbon, true);
        Motion.Pulse(CardApps, true);
        Motion.Pulse(CardBrowser, true);
        Motion.Pulse(CardScore, true);
        GuideBar.IsIndeterminate = true;
        ScanSnapshot snap;
        try
        {
            snap = await Task.Run(GoogleScanner.Scan);
        }
        finally
        {
            GuideBar.IsIndeterminate = false;
            Motion.ScanBusy(ScanRibbon, false);
            Motion.Pulse(CardApps, false);
            Motion.Pulse(CardBrowser, false);
            Motion.Pulse(CardScore, false);
        }
        _scan = snap;
        _apps.Clear();
        foreach (var app in snap.Apps) _apps.Add(app);
        GoogleScanner.ApplyToPlan(snap, _planRows);
        SavePlan();

        var realApps = snap.Apps.Count(a => !a.IsUpdater);
        StatApps.Text = realApps.ToString();
        StatAppsNote.Text = realApps == 0
            ? "Nothing Google-branded in installed programs."
            : $"{snap.Apps.Count} entries including updaters. Open This PC to review.";

        StatBrowser.Text = snap.DefaultBrowser;
        StatBrowserNote.Text = snap.DefaultBrowserIsGoogle
            ? "Windows still opens links in Chrome. Switch this before you uninstall Chrome."
            : "Default browser is not Chrome.";

        FolderText.Text = snap.GoogleFolders.Count == 0
            ? "No Google program folders found."
            : "Folders (profiles live here — do not delete by hand):\n" + string.Join("\n", snap.GoogleFolders);

        TaskText.Text = snap.GoogleTasks.Count == 0
            ? "No Google scheduled tasks found."
            : "Scheduled tasks: " + string.Join(", ", snap.GoogleTasks);

        var extra = new List<string>();
        if (snap.GoogleFolderChildren.Count > 0)
            extra.Add("Under Google folders: " + string.Join(", ", snap.GoogleFolderChildren.Distinct().Take(16)));
        if (snap.GoogleServices.Count > 0)
            extra.Add("Windows services: " + string.Join(", ", snap.GoogleServices.Distinct().Take(12)));
        if (snap.ChromeExtensions.Count > 0)
            extra.Add("Chrome extensions (Google-related): " + string.Join(", ", snap.ChromeExtensions.Distinct().Take(12)));
        if (snap.GoogleProcesses.Count > 0)
            extra.Add("Running now: " + string.Join(", ", snap.GoogleProcesses.Distinct()));
        if (snap.StartupEntries.Count > 0)
            extra.Add("Starts with Windows: " + string.Join(", ", snap.StartupEntries.Distinct()));
        if (snap.SignedInEmails.Count > 0)
            extra.Add("Chrome still signed into: " + string.Join(", ", snap.SignedInEmails));
        ExtraScanText.Text = extra.Count == 0
            ? "No extra Google services, extensions, or leftover accounts found beyond the list below."
            : string.Join("\n", extra);

        if (CenterHint is not null)
        {
            var leftovers = new List<string>();
            if (realApps > 0) leftovers.Add($"{realApps} Google app(s)");
            if (snap.DefaultBrowserIsGoogle) leftovers.Add("Chrome is still the default browser");
            if (snap.DnsLooksLikeGoogle) leftovers.Add("Google DNS");
            if (snap.SignedInEmails.Count > 0) leftovers.Add("Chrome Google login");
            CenterHint.Text = leftovers.Count == 0
                ? "No Google desktop leftovers jumped out. Drop a Takeout zip to inventory Gmail, Photos, Drive, and the rest — parsed only on this PC."
                : "Still on this PC: " + string.Join(" · ", leftovers) + ". Drop a Takeout zip to count mail, photos, and files before you disconnect anything.";
        }

        DnsText.Text = snap.DnsServers.Count == 0
            ? "Could not read DNS for the active adapter."
            : string.Join(", ", snap.DnsServers);
        DnsWarn.Text = snap.DnsLooksLikeGoogle
            ? "This looks like Google Public DNS."
            : "Not Google Public DNS.";

        EmptyScan.Visibility = snap.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PcSubtitle.Text = snap.Apps.Count == 0
            ? "No Google desktop apps registered on this PC."
            : $"{snap.Apps.Count} Google-related install(s). Uninstall only after you have a replacement.";

        UpdateGuideStats();
        SidebarHint.Text = "1 Scan · 2 Plan · 3 Takeout · 4 Verify — never auto-delete.";
        PrivacyStore.Log("scan", $"{realApps} apps");
        await Motion.StaggerPop(CardApps, CardBrowser, CardScore, CardCenter);
    }

    private void ShowScanSkipped()
    {
        StatApps.Text = "—";
        StatAppsNote.Text = "Scan needs your permission. Click Scan again on This PC, or turn it on in Privacy.";
        StatBrowser.Text = "—";
        StatBrowserNote.Text = "Not scanned.";
        PcSubtitle.Text = "This PC has not been scanned.";
        SidebarHint.Text = "Allow a scan when ready. Nothing leaves this PC until you choose.";
        if (FolderText is not null) FolderText.Text = "";
        if (TaskText is not null) TaskText.Text = "";
        if (ExtraScanText is not null) ExtraScanText.Text = "";
        if (CenterHint is not null)
            CenterHint.Text = "Scan this PC, then drop a Takeout zip. Everything stays local until you decide otherwise.";
    }

    private void ApplyHostedAiUi()
    {
        var show = StripeStore.HostedAiIsLive && StripeStore.LicenseApiLooksPublic;
        if (HostedAiBtn is not null)
            HostedAiBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateGuideStats()
    {
        var score = PlanStore.Score(_planRows);
        if (StatGuide is not null)
        {
            Motion.CountPercent(StatGuide, score.Percent);
            if (!GuideBar.IsIndeterminate)
                Motion.ProgressTo(GuideBar, score.Percent);
            if (_lastScore >= 0 && score.Percent != _lastScore)
                Motion.Pop(CardScore);
            _lastScore = score.Percent;
        }
        ScoreRemain.Text = score.RemainingHigh.Count == 0
            ? "No remaining weighted dependencies in your plan."
            : "Still on Google: " + string.Join(", ", score.RemainingHigh);
    }

    private void OnGuideItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GuideItem.IsDone)) return;
        ChecklistStore.Save(_guide);
        UpdateGuideStats();
        QueueCloudSync();
    }

    private void OnOpenDefaultApps(object sender, RoutedEventArgs e) =>
        TryOpenUri("ms-settings:defaultapps");

    private void OnOpenReplacement(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DetectedApp app } && !string.IsNullOrWhiteSpace(app.ReplacementUrl))
            TryOpenUri(app.ReplacementUrl);
    }

    private void OnOpenUrl(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }) TryOpenUri(url);
    }

    private async void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DetectedApp app }) return;

        if (app.IsDeveloperTool)
        {
            if (MessageBox.Show($"{app.Name} looks like a developer tool. Uninstalling it can break Android/GCP work.\n\nUninstall anyway?",
                    "Developer tool", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        if (!app.CanUninstall)
        {
            MessageBox.Show("Windows did not publish an uninstall command for this entry. Remove it from Settings → Apps.",
                "No uninstaller", MessageBoxButton.OK, MessageBoxImage.Information);
            TryOpenUri("ms-settings:appsfeatures");
            return;
        }

        if (!AskAccess.For(this, AccessKind.Uninstall,
                $"Uninstall {app.Name}?\n\nWindows will run the official uninstaller. Chrome/Drive profiles are not deleted by DeGoogle Kit."))
            return;

        try
        {
            LaunchUninstall(app.UninstallString);
            PrivacyStore.Log("uninstall_started", app.Name);
            var again = MessageBox.Show(
                "Windows opened the official uninstaller for " + app.Name +
                ".\n\nFinish that window, then click Yes so DeGoogle Kit can scan this PC again and tell you whether it is still installed. Profiles are not deleted.",
                "Uninstall started",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (again != MessageBoxResult.Yes) return;
            await RunScanAsync();
            var still = _apps.Any(a => a.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase));
            MessageBox.Show(
                still
                    ? app.Name + " is still listed in installed programs. If the uninstaller is still open, finish it and Scan again."
                    : app.Name + " is no longer in Add/Remove Programs. Folders or Chrome profiles may still be on disk — check This PC.",
                "Scan after uninstall",
                MessageBoxButton.OK,
                still ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start the uninstaller:\n{ex.Message}", "Uninstall failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnLocalPlan(object sender, RoutedEventArgs e) =>
        await AskUserAiAsync("Write a personal step-by-step de-Google plan for this PC. Be concrete, ordered, and honest about YouTube and anything without a 1:1 replacement. Never suggest Google products as the destination.");

    private void OnAskOffline(object sender, RoutedEventArgs e)
    {
        var q = ChatInput.Text.Trim();
        if (q.Length == 0) q = "what should I do first?";
        AddChat("You", q);
        ChatInput.Clear();
        AddChat("Coach", AiCoach.LocalAnswer(q, _scan, _guide));
    }

    private void OnOfflineTopics(object sender, RoutedEventArgs e) =>
        AddChat("Coach", OfflineGuide.Index());

    private void OnSetupAi(object sender, RoutedEventArgs e)
    {
        EnsureUserAi(forceSetup: true);
        CloudAiConsentBox.IsChecked = PrivacyStore.Consent.CloudAiConsent;
        SelectProvider(PrivacyStore.Settings.AiProvider);
    }

    private async void OnAskCloud(object sender, RoutedEventArgs e)
    {
        var q = ChatInput.Text.Trim();
        if (q.Length == 0) return;
        await AskUserAiAsync(q, showUser: true);
    }

    private bool EnsureUserAi(bool forceSetup = false)
    {
        if (!forceSetup && AiCoach.HasUserAi()) return true;
        var dialog = new AiSetupDialog { Owner = IsLoaded ? this : null };
        if (dialog.ShowDialog() != true) return false;
        if (dialog.UseOffline) return false;
        CloudAiConsentBox.IsChecked = true;
        SelectProvider(PrivacyStore.Settings.AiProvider);
        return AiCoach.HasUserAi();
    }

    private async Task AskUserAiAsync(string question, bool showUser = false)
    {
        if (!PersistAiSettings()) return;
        CapturePastedKey();
        if (CloudAiConsentBox.IsChecked == true)
            PrivacyStore.SetCloudAiConsent(true);
        if (!EnsureUserAi())
        {
            AddChat("Coach", AiCoach.LocalAnswer(question, _scan, _guide) +
                             "\n\n(Offline for now. Expand Connect, Get a key, paste it, tick consent, then Send — or stay on Offline/Topics.)");
            return;
        }

        var provider = PrivacyStore.Settings.AiProvider;
        if (!AskAccess.For(this, AccessKind.CloudAiSend,
                $"Send this chat turn and a local scan summary to {ProviderLabel(provider)}? Not Google. Uses your API key — you pay them, not us."))
            return;

        if (showUser)
        {
            AddChat("You", question);
            ChatInput.Clear();
        }
        AddChat("Coach", "Contacting " + ProviderLabel(provider) + "…");
        SetAiBusy(true, "Contacting " + ProviderLabel(provider) + "…");
        string answer;
        try
        {
            answer = await AiCoach.CloudAnswer(question, _scan, _guide, _chat.ToList(), CancellationToken.None);
        }
        finally
        {
            SetAiBusy(false);
        }
        if (_chat.Count > 0) _chat.RemoveAt(_chat.Count - 1);
        AddChat("Coach", answer);
        UpdateCoachConnectUi();
    }

    private void SetAiBusy(bool on, string? label = null)
    {
        if (AiTyping is null) return;
        if (!string.IsNullOrWhiteSpace(label))
            AiTyping.Text = label;
        AiTyping.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Motion.Pulse(AiTyping, on);
    }

    private static string ProviderLabel(string provider) => AiProviders.Find(provider).Name;

    private async void OnAskHosted(object sender, RoutedEventArgs e)
    {
        var q = ChatInput.Text.Trim();
        if (q.Length == 0) q = "what should I do first?";
        if (!StripeStore.HostedAiIsLive || !StripeStore.LicenseApiLooksPublic)
        {
            AddChat("Coach", UserFacing.HostedAiUnavailable());
            return;
        }
        if (!PrivacyStore.Consent.CloudAiConsent)
        {
            AddChat("Coach", UserFacing.HostedAiNeedsConsent());
            return;
        }
        if (!LicenseService.IsCloudPass)
        {
            AddChat("You", q);
            ChatInput.Clear();
            AddChat("Coach", AiCoach.HostedAnswerUnavailable());
            return;
        }
        if (!AskAccess.For(this, AccessKind.HostedAiSend))
            return;

        AddChat("You", q);
        ChatInput.Clear();
        AddChat("Coach", "Contacting DeGoogle AI…");
        SetAiBusy(true, "Contacting DeGoogle AI…");
        string answer;
        try
        {
            answer = await AiCoach.HostedAnswer(q, _scan, _guide, _chat.ToList(), CancellationToken.None);
        }
        finally
        {
            SetAiBusy(false);
        }
        if (_chat.Count > 0) _chat.RemoveAt(_chat.Count - 1);
        AddChat("Coach", answer);
    }

    private void OnSaveApiKey(object sender, RoutedEventArgs e)
    {
        if (!PersistAiSettings()) return;
        var key = ApiKeyBox.Password.Trim();
        if (key.Length >= 8)
        {
            if (!AskAccess.For(this, AccessKind.StoreSecret,
                    "Save this API key for your Windows user (DPAPI)? It is never sent to us or to Google by this save."))
                return;
            SecretStore.SaveApiKey(key);
            ApiKeyBox.Clear();
            UpdateApiKeyWatermark();
        }
        MessageBox.Show("Key saved on this Windows user. Tick consent if you have not, then Send. Keys use DPAPI and are never sent to us or to Google.",
            "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        UpdateCoachConnectUi();
    }

    private void OnAiProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || AiProviderBox?.SelectedItem is not ComboBoxItem item) return;
        var settings = PrivacyStore.Settings;
        settings.AiProvider = item.Tag as string ?? "groq";
        settings.AiModel = AiCoach.DefaultModel(settings.AiProvider);
        PrivacyStore.SaveSettings(settings);
        AiModelBox.Text = settings.AiModel;
        ApplyAiProviderUi();
        UpdateCoachConnectUi();
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e) => UpdateApiKeyWatermark();

    private void UpdateApiKeyWatermark()
    {
        if (ApiKeyWatermark is null || ApiKeyBox is null) return;
        ApiKeyWatermark.Visibility = ApiKeyBox.SecurePassword.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChatInputChanged(object sender, TextChangedEventArgs e)
    {
        if (ChatWatermark is null || ChatInput is null) return;
        ChatWatermark.Visibility = string.IsNullOrEmpty(ChatInput.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChatInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        OnAskCloud(sender, e);
    }
    private void OnOpenAiKeyClick(object sender, RoutedEventArgs e) => OpenCurrentAiKeyPage();
    private void OnOpenAiKeyPage(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenCurrentAiKeyPage();

    private void OpenCurrentAiKeyPage()
    {
        var def = CurrentAiProvider();
        TryOpenUri(def.KeyUrl);
    }

    private void FillAiProviders()
    {
        if (AiProviderBox is null) return;
        AiProviderBox.Items.Clear();
        foreach (var p in AiProviders.All)
            AiProviderBox.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
    }

    private static void MigrateRetiredGroqModel()
    {
        var settings = PrivacyStore.Settings;
        var next = AiProviders.NormalizeModel(settings.AiProvider, settings.AiModel);
        if (string.Equals(settings.AiModel, next, StringComparison.Ordinal)) return;
        settings.AiModel = next;
        PrivacyStore.SaveSettings(settings);
    }

    private void UpdateCoachConnectUi()
    {
        if (AiConnectHeader is null || AiConnectExpander is null) return;
        if (AiCoach.HasUserAi())
        {
            AiConnectHeader.Text = "AI connected — click to change key or provider";
            AiConnectExpander.IsExpanded = false;
        }
        else if (!string.IsNullOrWhiteSpace(SecretStore.LoadApiKey()) && !PrivacyStore.Consent.CloudAiConsent)
        {
            AiConnectHeader.Text = "Key saved — tick consent, then Send";
            AiConnectExpander.IsExpanded = true;
        }
        else
        {
            AiConnectHeader.Text = "Connect Groq once — Get a key, paste it, tick consent, then chat";
            AiConnectExpander.IsExpanded = true;
        }
    }

    private void CapturePastedKey()
    {
        if (ApiKeyBox is null) return;
        var pasted = ApiKeyBox.Password.Trim();
        if (pasted.Length < 8) return;
        var existing = SecretStore.LoadApiKey() ?? "";
        if (string.Equals(existing, pasted, StringComparison.Ordinal))
        {
            ApiKeyBox.Clear();
            UpdateApiKeyWatermark();
            return;
        }
        if (!AskAccess.For(this, AccessKind.StoreSecret,
                "Save this API key for your Windows user (DPAPI)? It is never sent to us or to Google by this save."))
            return;
        SecretStore.SaveApiKey(pasted);
        ApiKeyBox.Clear();
        UpdateApiKeyWatermark();
        UpdateCoachConnectUi();
    }

    private AiProviderDef CurrentAiProvider()
    {
        var id = (AiProviderBox.SelectedItem as ComboBoxItem)?.Tag as string
                 ?? PrivacyStore.Settings.AiProvider;
        return AiProviders.Find(id);
    }

    private void ApplyAiProviderUi()
    {
        var def = CurrentAiProvider();
        if (AiProviderHint is not null)
            AiProviderHint.Text = def.Hint;
        var showEndpoint = def.NeedsEndpoint ? Visibility.Visible : Visibility.Collapsed;
        if (EndpointLabel is not null) EndpointLabel.Visibility = showEndpoint;
        if (AiBaseUrlBox is not null)
        {
            AiBaseUrlBox.Visibility = showEndpoint;
            if (def.NeedsEndpoint && string.IsNullOrWhiteSpace(AiBaseUrlBox.Text))
                AiBaseUrlBox.Text = "http://127.0.0.1:11434/v1";
        }
    }

    private bool PersistAiSettings()
    {
        var def = CurrentAiProvider();
        var settings = PrivacyStore.Settings;
        settings.AiProvider = def.Id;
        settings.AiModel = AiProviders.NormalizeModel(def.Id, AiModelBox.Text);
        AiModelBox.Text = settings.AiModel;
        settings.AiBaseUrl = def.NeedsEndpoint ? AiBaseUrlBox.Text.Trim() : "";
        if (AiProviders.IsGoogleBlocked(def.Id, settings.AiModel, settings.AiBaseUrl))
        {
            MessageBox.Show("Google / Gemini is not allowed in DeGoogle Kit.", "Blocked");
            return false;
        }
        PrivacyStore.SaveSettings(settings);
        return true;
    }

    private void OnCloudConsent(object sender, RoutedEventArgs e)
    {
        PrivacyStore.SetCloudAiConsent(CloudAiConsentBox.IsChecked == true);
        UpdateCoachConnectUi();
    }

    private void OnCopyAccess(object sender, RoutedEventArgs e)
    {
        if (!AskAccess.For(this, AccessKind.Clipboard,
                "Copy the GDPR access / Takeout request template to the clipboard?"))
            return;
        Clipboard.SetText(GdprCatalog.AccessRequestTemplate);
        MessageBox.Show("Copied. Paste into Google’s privacy form or email. Fill in your address first.", "Clipboard");
    }

    private void OnCopyErasure(object sender, RoutedEventArgs e)
    {
        if (!AskAccess.For(this, AccessKind.Clipboard,
                "Copy the GDPR erasure request template to the clipboard?"))
            return;
        Clipboard.SetText(GdprCatalog.ErasureTemplate);
        MessageBox.Show("Copied. Export via Takeout before you send an erasure request.", "Clipboard");
    }

    private async void OnApplyDns(object sender, RoutedEventArgs e)
    {
        if (!RequirePro()) return;
        if (!AskAccess.For(this, AccessKind.DnsChange,
                "Switch this PC’s DNS to Quad9 (9.9.9.9 / 149.112.112.112)? Windows will ask for administrator permission. Current DNS is saved so you can restore it."))
            return;
        try
        {
            var result = DnsService.ApplyQuad9("");
            MessageBox.Show(result.Message, "DNS",
                MessageBoxButton.OK,
                result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await RunScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "DNS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnRestoreDns(object sender, RoutedEventArgs e)
    {
        if (!AskAccess.For(this, AccessKind.DnsChange,
                "Restore the DNS servers we saved before Quad9? Windows will ask for administrator permission."))
            return;
        var result = DnsService.Restore();
        MessageBox.Show(result.Message, "DNS",
            MessageBoxButton.OK,
            result.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await RunScanAsync();
    }

    private async void OnPickTakeoutFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Google Takeout folder" };
        if (dialog.ShowDialog() != true) return;
        await LoadTakeoutAsync(dialog.FolderName);
    }

    private async void OnPickTakeoutZip(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Takeout zip|*.zip", Title = "Google Takeout zip" };
        if (dialog.ShowDialog() != true) return;
        await LoadTakeoutAsync(dialog.FileName);
    }

    private async Task LoadTakeoutAsync(string path)
    {
        if (!AskAccess.For(this, AccessKind.TakeoutRead, path))
            return;
        TakeoutDropHint.Text = "Reading locally… " + path;
        GuideBar.IsIndeterminate = true;
        TakeoutInventory inv;
        try
        {
            inv = await Task.Run(() => TakeoutEngine.Analyze(path));
        }
        catch (Exception ex)
        {
            GuideBar.IsIndeterminate = false;
            TakeoutDropHint.Text = "Drop takeout-….zip here, or click to pick a file. Reading happens locally.";
            MessageBox.Show(ex.Message, "Takeout", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            GuideBar.IsIndeterminate = false;
        }

        _inventory = inv;
        TakeoutSummary.Text = inv.Summary;
        PasswordWarn.Text = inv.HasPasswordCsv
            ? "⚠️ A Chrome password CSV is in this archive. It is readable plaintext. Import into Proton Pass or Bitwarden, then delete the CSV."
            : "";
        _takeout.Clear();
        if (inv.Bundles.Count > 0)
        {
            foreach (var bundle in inv.Bundles)
                _takeout.Add(bundle);
        }
        else
        {
            foreach (var id in inv.DetectedServiceIds)
            {
                var def = ServiceCatalog.V1.FirstOrDefault(s => s.Id == id);
                _takeout.Add(TakeoutService.Entry(def?.GoogleName ?? id, 0));
            }
        }
        ApplyInventoryToPlan(inv);
        PrivacyStore.Log("takeout_scanned", path);
        NavTakeout.IsChecked = true;
        Motion.Pop(TakeoutDrop);
        TakeoutDropHint.Text = "Drop takeout-….zip here, or click to pick a file. Reading happens locally.";
    }

    private void ApplyInventoryToPlan(TakeoutInventory inv)
    {
        foreach (var row in _planRows)
        {
            if (!inv.DetectedServiceIds.Contains(row.Def.Id)) continue;
            row.Selected = true;
            if (row.Status == ServiceStatus.NotStarted)
                row.Status = ServiceStatus.ReadyToImport;
        }
        SavePlan();
        UpdateGuideStats();
    }

    private async void OnNormalizeTakeout(object sender, RoutedEventArgs e)
    {
        if (_inventory is null || string.IsNullOrWhiteSpace(_inventory.SourcePath))
        {
            MessageBox.Show("Drop or choose a Takeout archive first.", "Takeout");
            return;
        }
        if (!AskAccess.For(this, AccessKind.TakeoutConvert))
            return;
        TakeoutDropHint.Text = "Converting locally…";
        GuideBar.IsIndeterminate = true;
        try
        {
            var source = _inventory.SourcePath;
            var inv = _inventory;
            var dest = await Task.Run(() => TakeoutEngine.Normalize(source, inv));
            MessageBox.Show(
                "Converted on this PC:\n" + dest +
                "\n\nIncludes Keep → Markdown, Maps → GPX/KML/GeoJSON/CSV, YouTube subscriptions → OPML, and a password CSV warning if present.\n\nOpen the destination app and import. Do not delete Google yet.",
                "Local converter");
            TryOpenUri(dest);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Convert", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            GuideBar.IsIndeterminate = false;
            TakeoutDropHint.Text = "Drop takeout-….zip here, or click to pick a file. Reading happens locally.";
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (_dropLit || TakeoutDrop is null) return;
        _dropLit = true;
        Motion.Pulse(TakeoutDrop, true);
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        _dropLit = false;
        Motion.Pulse(TakeoutDrop, false);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        _dropLit = false;
        Motion.Pulse(TakeoutDrop, false);
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        await LoadTakeoutAsync(files[0]);
    }

    private void RebuildPlan()
    {
        if (PlanList is null) return;
        _planRows = PlanStore.Rows(_planState);
        foreach (var row in _planRows)
            row.PropertyChanged += (_, _) => SavePlan();
        PlanList.ItemsSource = _planRows;
        UpdateGuideStats();
    }

    private void SavePlan()
    {
        PlanStore.Persist(_planRows, _planState);
        QueueCloudSync();
    }

    private void ApplyModeRadios()
    {
        var mode = PlanStore.ParseMode(_planState.Mode);
        if (ModeEasy is null) return;
        ModeEasy.IsChecked = mode == MigrationMode.Easy;
        ModePrivacy.IsChecked = mode == MigrationMode.Privacy;
        ModeOwn.IsChecked = mode == MigrationMode.Own;
    }

    private void OnMode(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || PlanList is null || ModeEasy is null) return;
        _planState.Mode = ModePrivacy.IsChecked == true ? nameof(MigrationMode.Privacy)
            : ModeOwn.IsChecked == true ? nameof(MigrationMode.Own)
            : nameof(MigrationMode.Easy);
        RebuildPlan();
        SavePlan();
    }

    private void OnChromeBrave(object sender, RoutedEventArgs e)
    {
        _planState.ChromeChoice = "Brave";
        RebuildPlan();
        SavePlan();
    }

    private void OnChromeFirefox(object sender, RoutedEventArgs e)
    {
        _planState.ChromeChoice = "Firefox";
        RebuildPlan();
        SavePlan();
    }

    private void OnYtReduce(object sender, RoutedEventArgs e) { _planState.YoutubePath = "Reduce"; RebuildPlan(); SavePlan(); }
    private void OnYtLeave(object sender, RoutedEventArgs e) { _planState.YoutubePath = "Leave"; RebuildPlan(); SavePlan(); }
    private void OnYtArchive(object sender, RoutedEventArgs e) { _planState.YoutubePath = "Archive"; RebuildPlan(); SavePlan(); }

    private void OnOpenDestination(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlanRow row }) return;
        if (string.IsNullOrWhiteSpace(row.Destination.Url))
        {
            NavTakeout.IsChecked = true;
            return;
        }
        if (!TryOpenUri(row.Destination.Url)) return;
        if (row.Status == ServiceStatus.NotStarted)
            row.Status = row.Def.Level == MigrationLevel.Easy ? ServiceStatus.GoogleStillConnected : ServiceStatus.ExportRequired;
        SavePlan();
    }

    private void OnMarkImported(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlanRow row }) return;
        row.Status = ServiceStatus.Imported;
        SavePlan();
        UpdateGuideStats();
        MessageBox.Show("Imported is not the same as verified. Compare counts (photos, mail, places) before you disconnect Google.", "Do not delete yet");
    }

    private void OnMarkVerified(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlanRow row }) return;
        row.Status = ServiceStatus.ReadyToDisconnect;
        SavePlan();
        UpdateGuideStats();
        MessageBox.Show("Counts match on your side. It is now reasonable to disconnect this Google product — still a manual step, never automatic.", "Verified");
    }

    private void OnSaveReport(object sender, RoutedEventArgs e)
    {
        if (!RequirePro()) return;
        if (!AskAccess.For(this, AccessKind.DesktopExport,
                "Write an HTML report to your Desktop? It can include app names from this PC."))
            return;
        var path = ReportService.Write(_scan, _guide, _takeout, _planRows);
        MessageBox.Show("Saved to:\n" + path, "Report");
        TryOpenUri(path);
    }

    private async void OnStartTrial(object sender, RoutedEventArgs e)
    {
        if (!AccountService.IsSignedIn)
        {
            var go = MessageBox.Show(
                "A free account is required to start the " + LicenseService.TrialDays +
                "-day Pro trial. Sign in or create one now? No card is stored.",
                "Trial",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (go != MessageBoxResult.Yes) return;
            if (!AskAccess.For(this, AccessKind.AccountCloud))
                return;
            var dialog = new AccountDialog { Owner = this };
            if (dialog.ShowDialog() != true || !AccountService.IsSignedIn)
                return;
            RefreshAccountUi();
            _ = AccountService.TouchKeepaliveAsync();
            await RestoreAccountProgressAsync(interactive: false);
            await RestoreAccountLicenseAsync();
            RefreshLicenseUi();
        }

        if (LicenseService.IsPro && LicenseService.Record.Lifetime)
        {
            MessageBox.Show("This PC already has Lifetime Pro.", "Trial");
            return;
        }

        if (!AskAccess.For(this, AccessKind.ChangeLicense,
                "Start a " + LicenseService.TrialDays + "-day trial of Lifetime Pro on this account? No card is stored. Cloud Pass is not included."))
            return;

        var pull = await AccountService.PullProgressAsync();
        if (!pull.Ok)
        {
            MessageBox.Show(pull.Message, "Trial");
            return;
        }
        if (pull.Data?.TrialStartedAt is { } already)
        {
            LicenseService.ApplyCloudTrial(already);
            RefreshLicenseUi();
            MessageBox.Show(
                LicenseService.IsPro
                    ? "Your Pro trial is already running on this account."
                    : "This account already used the Pro trial.",
                "Trial");
            return;
        }

        if (LicenseService.IsPro)
        {
            MessageBox.Show("Pro extras are already on (" + LicenseService.StatusText + ").", "Trial");
            return;
        }
        if (LicenseService.Record.TrialEnds is not null)
        {
            MessageBox.Show("A trial was already used on this PC.", "Trial");
            return;
        }

        var claim = await AccountService.ClaimTrialAsync(ProgressCloud.GuideIds(_guide), _planState);
        if (claim.StartedAt is { } startedFromCloud && !claim.Ok)
        {
            LicenseService.ApplyCloudTrial(startedFromCloud);
            RefreshLicenseUi();
            MessageBox.Show(
                LicenseService.IsPro
                    ? "Your Pro trial is already running on this account."
                    : "This account already used the Pro trial.",
                "Trial");
            return;
        }
        if (!claim.Ok)
        {
            MessageBox.Show(claim.Message, "Trial");
            return;
        }

        if (!LicenseService.StartTrial())
        {
            MessageBox.Show("A trial was already used on this PC.", "Trial");
            return;
        }
        RefreshLicenseUi();
        MessageBox.Show("Lifetime Pro extras are on for " + LicenseService.TrialDays + " days. No card was stored.", "Trial");
    }

    private void OnBuyLifetime(object sender, RoutedEventArgs e)
    {
        if (!StripeStore.CatalogIsLive)
        {
            MessageBox.Show(
                "Checkout is not live yet (test mode). You can still use Free features and the 7-day trial with a free account. We’ll turn paid Checkout on when the live catalog is ready.",
                "Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryOpenUri(StripeStore.LifetimePaymentLink)) return;
        MessageBox.Show(StripeStore.AfterCheckoutHint, "Checkout");
    }

    private void OnBuyFamily(object sender, RoutedEventArgs e)
    {
        if (!StripeStore.CatalogIsLive)
        {
            MessageBox.Show(
                "Checkout is not live yet (test mode). Free features and the trial still work. Paid Family unlocks when live Checkout is on.",
                "Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryOpenUri(StripeStore.FamilyPaymentLink)) return;
        MessageBox.Show(StripeStore.AfterCheckoutHint, "Checkout");
    }

    protected override void OnClosed(EventArgs e)
    {
        _licenseWatch?.Dispose();
        _licenseWatch = null;
        _keepAliveClock.Stop();
        base.OnClosed(e);
    }

    private async void OnActivateKey(object sender, RoutedEventArgs e)
    {
        var raw = LicenseBox.Text.Trim();
        if (raw.Length == 0)
        {
            MessageBox.Show("Paste a license key or Checkout session id.", "License");
            return;
        }
        if (!AskAccess.For(this, AccessKind.ChangeLicense, "Activate a paid license on this PC?"))
            return;
        await RedeemAndInsertAsync(raw, interactive: true);
    }

    private void WatchPendingLicense()
    {
        AppPaths.EnsureRoot();
        _licenseWatch = new FileSystemWatcher(AppPaths.Root, ProtocolRegistration.PendingFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _licenseWatch.Changed += (_, _) => Dispatcher.InvokeAsync(RedeemLaunchLicenseAsync);
        _licenseWatch.Created += (_, _) => Dispatcher.InvokeAsync(RedeemLaunchLicenseAsync);
    }

    private async Task RedeemLaunchLicenseAsync()
    {
        var pending = ProtocolRegistration.TakePending();
        var launch = App.LaunchPayload;
        if (!string.IsNullOrWhiteSpace(launch))
            App.LaunchPayload = null;
        var raw = !string.IsNullOrWhiteSpace(pending) ? pending : launch;
        if (string.IsNullOrWhiteSpace(raw)) return;
        NavPro.IsChecked = true;
        await RedeemAndInsertAsync(raw, interactive: false);
    }

    private async Task RedeemAndInsertAsync(string raw, bool interactive)
    {
        if (Interlocked.Exchange(ref _licenseRedeemBusy, 1) == 1) return;
        try
        {
            var result = await LicenseClient.RedeemAsync(raw);
            if (!result.Ok || string.IsNullOrWhiteSpace(result.Key))
            {
                if (interactive || ProtocolRegistration.LooksLikePayload(raw))
                    MessageBox.Show(result.Message, "License");
                return;
            }

            if (!LicenseService.Activate(result.Key))
            {
                MessageBox.Show("The server issued a key this copy of DeGoogle Kit could not verify.", "License");
                return;
            }

            LicenseBox.Text = result.Key;
            RefreshLicenseUi();
            NavPro.IsChecked = true;
            var cloud = !string.IsNullOrWhiteSpace(LicenseService.Record.CloudKey)
                        && string.Equals(LicenseService.Record.CloudKey, result.Key, StringComparison.OrdinalIgnoreCase);
            if (AccountService.IsSignedIn && !cloud)
            {
                var bind = await AccountService.BindLicenseAsync(result.Key);
                if (interactive && !bind.Ok)
                    MessageBox.Show(bind.Message + " Pro still works on this PC.", "Account");
            }
            if (interactive || !string.IsNullOrWhiteSpace(raw))
            {
                if (cloud)
                {
                    var until = LicenseService.Record.CloudPassEnds is { } end ? " until " + end.ToString("d") : "";
                    MessageBox.Show("Cloud Pass is on" + until + ". DeGoogle AI uses Groq GPT-OSS 120B (we pay). It is not billed until the live Stripe catalog is on.", "License");
                }
                else
                {
                    var extra = LicenseService.Record.Seats > 1
                        ? " Family covers up to " + LicenseService.Record.Seats + " PCs — copy the key below onto the others."
                        : "";
                    MessageBox.Show("Pro is on. The signed license key is in the box on the Pro tab." + extra, "License");
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _licenseRedeemBusy, 0);
        }
    }

    private async void OnAccountSignIn(object sender, RoutedEventArgs e)
    {
        if (!AskAccess.For(this, AccessKind.AccountCloud))
            return;
        var dialog = new AccountDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        RefreshAccountUi();
        _ = AccountService.TouchKeepaliveAsync();
        await RestoreAccountProgressAsync(interactive: true);
        await RestoreAccountLicenseAsync();
        RefreshLicenseUi();
    }

    private async void OnAccountSignOut(object sender, RoutedEventArgs e)
    {
        _cloudSyncTimer.Stop();
        await AccountService.SignOutAsync();
        RefreshAccountUi();
        MessageBox.Show(
            "Signed out. This PC still has your local plan and checklist. The free account keeps the cloud backup. Pro stays if you already activated a key here.",
            "Account");
    }

    private void RefreshAccountUi()
    {
        if (AccountStatus is null) return;
        AccountStatus.Text = AccountService.StatusText;
    }

    private void QueueCloudSync()
    {
        if (!_uiReady || _cloudApplying || !AccountService.IsSignedIn || !PermissionService.AccountCloudGranted) return;
        _cloudSyncTimer.Stop();
        _cloudSyncTimer.Start();
    }

    private async Task PushCloudProgressAsync()
    {
        if (!AccountService.IsSignedIn || _cloudApplying) return;
        await AccountService.PushProgressAsync(ProgressCloud.GuideIds(_guide), _planState);
    }

    private async Task RestoreAccountProgressAsync(bool interactive)
    {
        if (!AccountService.IsSignedIn) return;
        var pull = await AccountService.PullProgressAsync();
        if (!pull.Ok)
        {
            if (interactive)
                MessageBox.Show(pull.Message, "Account");
            return;
        }

        _cloudApplying = true;
        try
        {
            if (pull.Data is { } remote)
            {
                ProgressCloud.ApplyGuide(_guide, remote.GuideDone);
                _planState = ProgressCloud.MergePlan(_planState, remote.Plan);
                PlanStore.Save(_planState);
                ChecklistStore.Save(_guide);
                RebuildPlan();
                ApplyModeRadios();
                UpdateGuideStats();
                if (remote.TrialStartedAt is { } trialAt)
                {
                    LicenseService.ApplyCloudTrial(trialAt);
                    RefreshLicenseUi();
                }
            }
        }
        finally
        {
            _cloudApplying = false;
        }

        var push = await AccountService.PushProgressAsync(ProgressCloud.GuideIds(_guide), _planState);
        PrivacyStore.Log("progress_restored", AccountService.Session?.Email ?? "");
        if (!interactive) return;
        var msg = pull.Data is null
            ? "This free account had no backup yet. This PC's plan and checklist are now saved to it."
            : "Your free account backup was merged with this PC (plan and checklist). API keys and Takeout files stay local.";
        if (!push.Ok) msg += "\n\nCloud save: " + push.Message;
        MessageBox.Show(msg, "Account");
    }

    private async Task RestoreAccountLicenseAsync()
    {
        if (!AccountService.IsSignedIn) return;
        var pull = await AccountService.PullLicenseAsync();
        if (!pull.Ok || string.IsNullOrWhiteSpace(pull.Key)) return;
        if (LicenseService.Activate(pull.Key))
        {
            LicenseBox.Text = pull.Key;
            RefreshLicenseUi();
            PrivacyStore.Log("account_license_restored", AccountService.Session?.Email ?? "");
        }
    }

    private void OnAddDesktopShortcut(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            ShortcutService.CreateDesktop()
                ? "Desktop shortcut added. It opens this DeGoogleKit.exe."
                : "Could not create the Desktop shortcut.",
            "Shortcut");
    }

    private void OnAddStartMenuShortcut(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            ShortcutService.CreateStartMenu()
                ? "Start menu shortcut added. It opens this DeGoogleKit.exe."
                : "Could not create the Start menu shortcut.",
            "Shortcut");
    }

    private async void OnExportData(object sender, RoutedEventArgs e)
    {
        if (!AskAccess.For(this, AccessKind.DesktopExport,
                "Export DeGoogle Kit data as a zip on your Desktop? Session tokens are left out. If you are signed in, the zip includes a copy of your cloud plan/checklist."))
            return;
        await AccountService.WriteExportSnapshotAsync();
        var path = PrivacyStore.ExportArchive();
        MessageBox.Show("Exported to:\n" + path, "Export");
        TryOpenUri(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    }

    private async void OnDeleteData(object sender, RoutedEventArgs e)
    {
        if (AccountService.IsSignedIn)
        {
            var cloud = MessageBox.Show(
                "Also delete your cloud account (email, plan backup, linked Pro on the account)? This PC’s files are separate and will still be deleted next if you continue.",
                "Cloud account",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (cloud == MessageBoxResult.Cancel) return;
            if (cloud == MessageBoxResult.Yes)
            {
                if (!AskAccess.For(this, AccessKind.DeleteAccount))
                    return;
                var del = await AccountService.DeleteAccountAsync();
                if (!del.Ok)
                {
                    MessageBox.Show(del.Message, "Account");
                    return;
                }
            }
        }
        if (!AskAccess.For(this, AccessKind.EraseAppData))
            return;
        PrivacyStore.DeleteAllLocalData();
        Application.Current.Shutdown();
    }

    private async void OnDeleteCloudAccount(object sender, RoutedEventArgs e)
    {
        if (!AccountService.IsSignedIn)
        {
            MessageBox.Show("Sign in first, or there is no cloud account on this PC.", "Account");
            return;
        }
        if (!AskAccess.For(this, AccessKind.DeleteAccount))
            return;
        var del = await AccountService.DeleteAccountAsync();
        RefreshAccountUi();
        MessageBox.Show(del.Message, "Account");
    }

    private void OnOpenPrivacyNotice(object sender, RoutedEventArgs e) => TryOpenUri(LegalCopy.PrivacyUrl);

    private void OnOpenTerms(object sender, RoutedEventArgs e) => TryOpenUri(LegalCopy.TermsUrl);

    private void OnOpenWebsiteAccount(object sender, RoutedEventArgs e) => TryOpenUri(LegalCopy.WebsiteAccountUrl);

    private bool RequirePro()
    {
        if (LicenseService.IsPro) return true;
        NavPro.IsChecked = true;
        MessageBox.Show("That feature is Lifetime Pro (" + LicenseService.LifetimePrice +
                        " once). Sign in to start the " + LicenseService.TrialDays +
                        "-day trial (no card) or activate a key. Scan, plan, Takeout, local coach, and GDPR stay free.",
            "Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void RefreshLicenseUi()
    {
        LicenseBadge.Text = LicenseService.StatusText;
        ProStatus.Text = "Current plan: " + LicenseService.StatusText;
        var stored = LicenseService.Record.Key;
        if (!string.IsNullOrWhiteSpace(stored)
            && stored.StartsWith("DGK2.", StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(LicenseBox.Text)
                || LicenseBox.Text.Trim().StartsWith("cs_", StringComparison.OrdinalIgnoreCase)
                || LicenseBox.Text.Trim().StartsWith("DGK2.", StringComparison.Ordinal)))
            LicenseBox.Text = stored;
        if (!_uiReady || LicenseBadge is null) return;
        Motion.Pop(LicenseBadge);
        if (LicenseService.IsPro)
            Motion.Pop(ProLifetimeCard);
    }

    private void RefreshAudit() => AuditList.ItemsSource = PrivacyStore.Audit().AsEnumerable().Reverse().Take(30).ToList();

    private void SelectProvider(string provider)
    {
        foreach (var item in AiProviderBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, provider, StringComparison.OrdinalIgnoreCase))
            {
                AiProviderBox.SelectedItem = item;
                break;
            }
        }
    }

    private void AddChat(string role, string text)
    {
        _chat.Add(new ChatMessage { Role = role, Text = text });
        ChatList.ScrollIntoView(_chat[^1]);
    }

    private static void LaunchUninstall(string uninstallString)
    {
        var (file, args) = SplitCommand(uninstallString);
        Process.Start(new ProcessStartInfo { FileName = file, Arguments = args, UseShellExecute = true });
    }

    private static (string File, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
        }
        var space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }

    private void BindPermissionToggles()
    {
        if (PermScan is null) return;
        _permUiReady = false;
        PermScan.IsChecked = PermissionService.Get(AccessKind.PcScan) == true;
        PermLinks.IsChecked = PermissionService.Get(AccessKind.OpenLink) == true;
        PermClipboard.IsChecked = PermissionService.Get(AccessKind.Clipboard) == true;
        PermUpdates.IsChecked = PermissionService.Get(AccessKind.UpdateCheck) == true;
        PermTakeout.IsChecked = PermissionService.Get(AccessKind.TakeoutRead) == true;
        PermExport.IsChecked = PermissionService.Get(AccessKind.DesktopExport) == true;
        PermSecrets.IsChecked = PermissionService.Get(AccessKind.StoreSecret) == true;
        PermAccount.IsChecked = PermissionService.Get(AccessKind.AccountCloud) == true;
        _permUiReady = true;
    }

    private void OnPermissionToggle(object sender, RoutedEventArgs e)
    {
        if (!_permUiReady) return;
        PermissionService.Set(AccessKind.PcScan, PermScan.IsChecked == true);
        PermissionService.Set(AccessKind.OpenLink, PermLinks.IsChecked == true);
        PermissionService.Set(AccessKind.Clipboard, PermClipboard.IsChecked == true);
        PermissionService.Set(AccessKind.UpdateCheck, PermUpdates.IsChecked == true);
        PermissionService.Set(AccessKind.TakeoutRead, PermTakeout.IsChecked == true);
        PermissionService.Set(AccessKind.DesktopExport, PermExport.IsChecked == true);
        PermissionService.Set(AccessKind.StoreSecret, PermSecrets.IsChecked == true);
        PermissionService.Set(AccessKind.AccountCloud, PermAccount.IsChecked == true);
        if (PermAccount.IsChecked != true && AccountService.IsSignedIn)
        {
            _cloudSyncTimer.Stop();
            _ = AccountService.SignOutAsync();
            RefreshAccountUi();
        }
    }

    private bool TryOpenUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        var kind = uri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ? AccessKind.OpenSettings
            : uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? AccessKind.OpenLink
            : AccessKind.OpenFolder;
        if (!AskAccess.For(this, kind, uri)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
