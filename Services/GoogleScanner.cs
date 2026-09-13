using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DeGoogleKit.Models;
using Microsoft.Win32;

namespace DeGoogleKit.Services;

public static class GoogleScanner
{
    private static readonly Dictionary<string, (string Name, string Url)> Replacements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = ("Firefox, Brave, or LibreWolf", "https://www.mozilla.org/firefox/"),
        ["drive"] = ("Proton Drive or Nextcloud", "https://proton.me/drive"),
        ["backup and sync"] = ("Proton Drive or Nextcloud", "https://proton.me/drive"),
        ["earth"] = ("Organic Maps or OpenStreetMap", "https://organicmaps.app/"),
        ["remote desktop"] = ("RustDesk or AnyDesk", "https://rustdesk.com/"),
        ["play games"] = ("Uninstall if unused", "https://f-droid.org/"),
        ["japanese"] = ("Windows IME", "ms-settings:regionlanguage"),
        ["cloud sdk"] = ("Keep if you develop with GCP", "https://cloud.google.com/sdk"),
        ["android studio"] = ("Keep if you build Android apps", "https://developer.android.com/studio"),
        ["nearby share"] = ("LocalSend", "https://localsend.org/"),
        ["quick share"] = ("LocalSend", "https://localsend.org/"),
        ["web designer"] = ("Figma or Penpot", "https://penpot.app/"),
        ["ads editor"] = ("Uninstall if you left Ads", "https://ads.google.com/"),
        ["meet"] = ("Jitsi Meet or Signal", "https://meet.jit.si/"),
        ["youtube"] = ("FreeTube (still YouTube catalog)", "https://freetubeapp.io/"),
    };

    public static ScanSnapshot Scan()
    {
        var snap = new ScanSnapshot();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ScanUninstall(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", "User", snap, seen);
        ScanUninstall(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Machine", snap, seen);
        ScanUninstall(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "Machine (32-bit)", snap, seen);

        snap.Apps.Sort((a, b) =>
        {
            var rank = Rank(a).CompareTo(Rank(b));
            return rank != 0 ? rank : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        DetectDefaultBrowser(snap);
        DetectFolders(snap);
        DetectTasks(snap);
        DetectServices(snap);
        DetectChromeExtensions(snap);
        DetectProcesses(snap);
        DetectStartup(snap);
        DetectChromeAccounts(snap);
        DetectAppx(snap);
        DnsService.AttachToScan(snap);
        return snap;
    }

    public static void ApplyToPlan(ScanSnapshot scan, IEnumerable<PlanRow> rows)
    {
        var names = string.Join(' ', scan.Apps.Select(a => a.Name));
        var blob = string.Join(' ',
            names,
            string.Join(' ', scan.GoogleProcesses),
            string.Join(' ', scan.StartupEntries),
            string.Join(' ', scan.GoogleFolderChildren),
            string.Join(' ', scan.ChromeExtensions),
            string.Join(' ', scan.GoogleServices));
        var chromeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome");
        var chromePresent = scan.DefaultBrowserIsGoogle
            || blob.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(chromeDir);
        foreach (var row in rows)
        {
            if (row.Status is not ServiceStatus.NotStarted) continue;
            var hit = row.Def.Id switch
            {
                "chrome" => chromePresent,
                "drive" => blob.Contains("Drive", StringComparison.OrdinalIgnoreCase),
                "meet" => blob.Contains("Meet", StringComparison.OrdinalIgnoreCase),
                "earth" => blob.Contains("Earth", StringComparison.OrdinalIgnoreCase),
                "dns" => scan.DnsLooksLikeGoogle,
                "passwords" => chromePresent || scan.SignedInEmails.Count > 0,
                "youtube" => blob.Contains("YouTube", StringComparison.OrdinalIgnoreCase),
                "search" => scan.DefaultBrowserIsGoogle,
                _ => false
            };
            if (hit) row.Status = ServiceStatus.GoogleStillConnected;
        }
    }

    private static int Rank(DetectedApp app)
    {
        if (app.IsUpdater) return 2;
        if (app.IsDeveloperTool) return 1;
        return 0;
    }

    private static void ScanUninstall(RegistryKey hive, string path, string scope, ScanSnapshot snap, HashSet<string> seen)
    {
        using var root = hive.OpenSubKey(path);
        if (root is null) return;

        foreach (var subName in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(subName);
            if (key is null) continue;

            var name = key.GetValue("DisplayName") as string;
            var publisher = key.GetValue("Publisher") as string ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!IsGoogleProduct(name, publisher)) continue;

            var id = $"{scope}|{name}|{key.GetValue("DisplayVersion")}";
            if (!seen.Add(id)) continue;

            var replacement = MatchReplacement(name);
            snap.Apps.Add(new DetectedApp
            {
                Name = name.Trim(),
                Publisher = publisher.Trim(),
                Version = (key.GetValue("DisplayVersion") as string ?? "").Trim(),
                Scope = scope,
                UninstallString = (key.GetValue("UninstallString") as string ?? "").Trim(),
                IsDeveloperTool = IsDeveloper(name),
                IsUpdater = IsUpdater(name),
                Replacement = replacement.Name,
                ReplacementUrl = replacement.Url
            });
        }
    }

    private static bool IsGoogleProduct(string name, string publisher)
    {
        var n = name.ToLowerInvariant();
        var p = publisher.ToLowerInvariant();

        if (p.Contains("google") || p.Contains("youtube llc") || p.Contains("alphabet")) return true;
        if (n.Contains("google chrome") || n.StartsWith("google ")) return true;
        if (n.Contains("chrome remote desktop")) return true;
        if (n.Contains("backup and sync from google")) return true;
        if (n.Contains("google drive") || n.Contains("drive for desktop")) return true;
        if (n.Contains("nearby share") || n.Contains("quick share")) return true;
        if (n.Contains("google earth") || n.Contains("google meet") || n.Contains("google chat")) return true;
        if (n.Contains("google japanese") || n.Contains("google ime")) return true;
        if (n.Contains("chromecast") || n.Contains("google play")) return true;
        if (n.Contains("gmail") && p.Contains("google")) return true;
        if (n.Contains("youtube music") || n.Contains("google nest") || n.Contains("chromebook")) return true;
        if (n.Contains("google usb") || n.Contains("android usb")) return true;
        return false;
    }

    private static bool IsDeveloper(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("android studio") || n.Contains("cloud sdk") || n.Contains("flutter") || n.Contains("dart");
    }

    private static bool IsUpdater(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("google update") || n.Contains("google installer") || n.Contains("google software update");
    }

    private static (string Name, string Url) MatchReplacement(string name)
    {
        foreach (var (key, value) in Replacements)
        {
            if (name.Contains(key, StringComparison.OrdinalIgnoreCase)) return value;
        }
        return ("Pick a non-Google alternative in the Guide tab", "");
    }

    private static void DetectDefaultBrowser(ScanSnapshot snap)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var progId = key?.GetValue("ProgId") as string ?? "";
            snap.DefaultBrowser = DescribeBrowser(progId);
            snap.DefaultBrowserIsGoogle = progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
                && !progId.Contains("Brave", StringComparison.OrdinalIgnoreCase)
                && !progId.Contains("Edge", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            snap.DefaultBrowser = "Could not read";
        }
    }

    private static string DescribeBrowser(string progId)
    {
        if (string.IsNullOrWhiteSpace(progId)) return "Not set";
        if (progId.Contains("Brave", StringComparison.OrdinalIgnoreCase)) return "Brave";
        if (progId.Contains("ChromeHTML", StringComparison.OrdinalIgnoreCase) ||
            progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (progId.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (progId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (progId.Contains("LibreWolf", StringComparison.OrdinalIgnoreCase)) return "LibreWolf";
        if (progId.Contains("Opera", StringComparison.OrdinalIgnoreCase)) return "Opera";
        return progId;
    }

    private static void DetectFolders(ScanSnapshot snap)
    {
        string[] paths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google")
        ];

        foreach (var path in paths)
        {
            if (!Directory.Exists(path)) continue;
            snap.GoogleFolders.Add(path);
            try
            {
                foreach (var child in Directory.GetDirectories(path))
                    snap.GoogleFolderChildren.Add(Path.GetFileName(child));
            }
            catch
            {
                // Folder listing is best-effort.
            }
        }
    }

    private static void DetectTasks(ScanSnapshot snap)
    {
        try
        {
            using var tree = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree");
            if (tree is null) return;
            WalkTasks(tree, "", snap.GoogleTasks, 0);
        }
        catch
        {
            // Reading the task cache is best-effort.
        }
    }

    private static void WalkTasks(RegistryKey key, string prefix, List<string> found, int depth)
    {
        if (depth > 6) return;
        foreach (var name in key.GetSubKeyNames())
        {
            var path = string.IsNullOrEmpty(prefix) ? name : prefix + "\\" + name;
            if (path.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("GUpdate", StringComparison.OrdinalIgnoreCase))
                found.Add(path);

            try
            {
                using var child = key.OpenSubKey(name);
                if (child is not null) WalkTasks(child, path, found, depth + 1);
            }
            catch
            {
                // Skip locked task keys.
            }
        }
    }

    private static void DetectServices(ScanSnapshot snap)
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (root is null) return;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key is null) continue;
                var display = key.GetValue("DisplayName") as string ?? name;
                var image = key.GetValue("ImagePath") as string ?? "";
                var blob = display + " " + image + " " + name;
                if (!blob.Contains("Google", StringComparison.OrdinalIgnoreCase) &&
                    !blob.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (blob.Contains("Chromium", StringComparison.OrdinalIgnoreCase) &&
                    !blob.Contains("Google", StringComparison.OrdinalIgnoreCase))
                    continue;
                snap.GoogleServices.Add(display);
            }
        }
        catch
        {
            // Services list is best-effort.
        }
    }

    private static void DetectChromeExtensions(ScanSnapshot snap)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data");
            if (!Directory.Exists(root)) return;
            foreach (var profile in Directory.GetDirectories(root))
            {
                var extRoot = Path.Combine(profile, "Extensions");
                if (!Directory.Exists(extRoot)) continue;
                foreach (var extDir in Directory.GetDirectories(extRoot))
                {
                    var id = Path.GetFileName(extDir);
                    if (id.Length < 16) continue;
                    var label = ReadExtensionName(extDir) ?? id;
                    if (IsGoogleRelatedExtension(label, extDir))
                        snap.ChromeExtensions.Add(label);
                }
            }
        }
        catch
        {
            // Chrome profile walk is best-effort.
        }
    }

    private static bool IsGoogleRelatedExtension(string label, string extDir)
    {
        if (label.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Gmail", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Docs", StringComparison.OrdinalIgnoreCase) && label.Contains("Drive", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var version = Directory.GetDirectories(extDir).FirstOrDefault();
            if (version is null) return false;
            var manifest = Path.Combine(version, "manifest.json");
            if (!File.Exists(manifest)) return false;
            var json = File.ReadAllText(manifest);
            if (json.Contains("clients2.google.com", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase))
                return false;
            return json.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase)
                || json.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase)
                || json.Contains("mail.google.com", StringComparison.OrdinalIgnoreCase)
                || json.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase)
                || json.Contains("youtube.com", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadExtensionName(string extDir)
    {
        try
        {
            var version = Directory.GetDirectories(extDir).FirstOrDefault();
            if (version is null) return null;
            var manifest = Path.Combine(version, "manifest.json");
            if (!File.Exists(manifest)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
            return doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static void DetectProcesses(ScanSnapshot snap)
    {
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName;
                    if (!IsGoogleProcess(name)) continue;
                    if (!snap.GoogleProcesses.Contains(name, StringComparer.OrdinalIgnoreCase))
                        snap.GoogleProcesses.Add(name);
                }
                catch
                {
                    // Skip processes we cannot inspect.
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Process list is best-effort.
        }
    }

    private static bool IsGoogleProcess(string name)
    {
        var n = name.ToLowerInvariant();
        if (n is "chrome" or "googlecrashhandler" or "googlecrashhandler64" or "googleupdate"
            or "googleupdatebroker" or "googleupdatecomregistershell64" or "googleupdatesetup"
            or "googledrivefs" or "googledrivesync" or "googleearth" or "googleearthpro"
            or "meet" or "chrome_proxy" or "chrome_pwa_launcher")
            return true;
        return n.StartsWith("google") && n is not "googleupdatecore";
    }

    private static void DetectStartup(ScanSnapshot snap)
    {
        ScanRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", snap);
        ScanRunKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", snap);
        ScanRunKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", snap);
    }

    private static void ScanRunKey(RegistryKey hive, string path, ScanSnapshot snap)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name)?.ToString() ?? "";
                var blob = name + " " + value;
                if (!blob.Contains("Google", StringComparison.OrdinalIgnoreCase) &&
                    !blob.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                    !blob.Contains("DriveFS", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (blob.Contains("Brave", StringComparison.OrdinalIgnoreCase) ||
                    blob.Contains("Chromium", StringComparison.OrdinalIgnoreCase))
                    continue;
                snap.StartupEntries.Add(name);
            }
        }
        catch
        {
            // Startup keys are best-effort.
        }
    }

    private static void DetectChromeAccounts(ScanSnapshot snap)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data");
            if (!Directory.Exists(root)) return;
            foreach (var profile in Directory.GetDirectories(root))
            {
                var prefs = Path.Combine(profile, "Preferences");
                if (!File.Exists(prefs)) continue;
                ReadChromeEmails(prefs, snap);
            }
        }
        catch
        {
            // Chrome profile read is best-effort.
        }
    }

    private static void ReadChromeEmails(string prefs, ScanSnapshot snap)
    {
        try
        {
            var info = new FileInfo(prefs);
            if (info.Length is <= 0 or > 30_000_000) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(prefs));
            if (doc.RootElement.TryGetProperty("account_info", out var accounts) &&
                accounts.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in accounts.EnumerateArray())
                {
                    if (item.TryGetProperty("email", out var email))
                        AddEmail(snap, email.GetString());
                }
            }
            if (doc.RootElement.TryGetProperty("signin", out var signin) &&
                signin.ValueKind == JsonValueKind.Object &&
                signin.TryGetProperty("allowed_username", out var user))
                AddEmail(snap, user.GetString());
        }
        catch
        {
            // Preferences JSON can be incomplete while Chrome is running.
        }
    }

    private static void AddEmail(ScanSnapshot snap, string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return;
        if (!snap.SignedInEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
            snap.SignedInEmails.Add(email);
    }

    private static void DetectAppx(ScanSnapshot snap)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (root is null) return;
            foreach (var name in root.GetSubKeyNames())
            {
                if (!name.Contains("Google", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.Contains("Brave", StringComparison.OrdinalIgnoreCase)) continue;
                var label = name.Split('_')[0];
                if (snap.Apps.Any(a => a.Name.Contains(label, StringComparison.OrdinalIgnoreCase))) continue;
                if (!snap.GoogleFolderChildren.Contains(label, StringComparer.OrdinalIgnoreCase))
                    snap.GoogleFolderChildren.Add("App: " + label);
            }
        }
        catch
        {
            // Store package list is best-effort.
        }
    }
}
