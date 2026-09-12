using System.IO;
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
        ["web designer"] = ("Figma or Penpot", "https://penpot.app/"),
        ["ads editor"] = ("Uninstall if you left Ads", "https://ads.google.com/"),
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
        DnsService.AttachToScan(snap);
        return snap;
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

        if (p.Contains("google")) return true;
        if (n.Contains("google chrome")) return true;
        if (n.StartsWith("google ")) return true;
        if (n.Contains("chrome remote desktop")) return true;
        if (n.Contains("backup and sync from google")) return true;
        if (n.Contains("google drive")) return true;
        if (n.Contains("nearby share")) return true;
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
        return n.Contains("google update") || n.Contains("google installer");
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
            snap.DefaultBrowserIsGoogle = progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            snap.DefaultBrowser = "Could not read";
        }
    }

    private static string DescribeBrowser(string progId)
    {
        if (string.IsNullOrWhiteSpace(progId)) return "Not set";
        if (progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (progId.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (progId.Contains("Brave", StringComparison.OrdinalIgnoreCase)) return "Brave";
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
            if (Directory.Exists(path)) snap.GoogleFolders.Add(path);
        }
    }

    private static void DetectTasks(ScanSnapshot snap)
    {
        try
        {
            using var tree = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree");
            if (tree is null) return;

            foreach (var name in tree.GetSubKeyNames())
            {
                if (name.Contains("Google", StringComparison.OrdinalIgnoreCase))
                {
                    snap.GoogleTasks.Add(name);
                    continue;
                }

                using var child = tree.OpenSubKey(name);
                if (child is null) continue;
                foreach (var nested in child.GetSubKeyNames())
                {
                    if (nested.Contains("Google", StringComparison.OrdinalIgnoreCase))
                    {
                        snap.GoogleTasks.Add($"{name}\\{nested}");
                    }
                }
            }
        }
        catch
        {
            // Reading the task cache is best-effort.
        }
    }
}
