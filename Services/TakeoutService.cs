using System.IO;
using System.IO.Compression;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class TakeoutService
{
    private static readonly Dictionary<string, string> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Drive"] = "Move to Proton Drive or Nextcloud",
        ["Mail"] = "Import MBOX into Proton/Tuta/Thunderbird",
        ["YouTube and YouTube Music"] = "Subscriptions → FreeTube / NewPipe",
        ["YouTube"] = "Subscriptions → FreeTube / NewPipe",
        ["Google Photos"] = "Import into Ente or Immich",
        ["Takeout"] = "Top-level Google Takeout folder",
        ["Chrome"] = "Bookmarks/passwords — import to Firefox/Bitwarden",
        ["Calendar"] = "Import ICS into Proton Calendar",
        ["Contacts"] = "Import vCard into your new mail host",
        ["Maps"] = "Saved places — Organic Maps cannot import all of these",
        ["Fit"] = "Health data — keep the export",
        ["Play Store"] = "App list for F-Droid / Aurora later",
        ["My Activity"] = "Sensitive history — store encrypted or delete after review",
        ["Business Profile"] = "If you have a company listing, handle separately",
        ["Google Pay"] = "Payment methods — add them to your bank/PayPal instead",
        ["Location History"] = "Highly sensitive — do not upload to random clouds"
    };

    public static List<TakeoutEntry> Analyze(string path)
    {
        if (Directory.Exists(path)) return FromFolder(path);
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return FromZip(path);
        throw new InvalidOperationException("Pick a Takeout folder or .zip file.");
    }

    private static List<TakeoutEntry> FromFolder(string path)
    {
        var entries = new List<TakeoutEntry>();
        foreach (var dir in Directory.GetDirectories(path))
        {
            var name = Path.GetFileName(dir);
            entries.Add(NewEntry(name, DirSize(dir)));
        }
        foreach (var file in Directory.GetFiles(path))
        {
            entries.Add(NewEntry(Path.GetFileName(file), new FileInfo(file).Length));
        }
        return entries.OrderByDescending(e => e.SizeBytes).ToList();
    }

    private static List<TakeoutEntry> FromZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var bags = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName) || entry.FullName.EndsWith('/')) continue;
            var parts = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var folder = parts.Length == 0 ? entry.Name : parts[0];
            bags.TryGetValue(folder, out var n);
            bags[folder] = n + entry.Length;
        }
        return bags.Select(kv => NewEntry(kv.Key, kv.Value)).OrderByDescending(e => e.SizeBytes).ToList();
    }

    private static TakeoutEntry NewEntry(string name, long size) =>
        new()
        {
            Name = name,
            SizeBytes = size,
            Hint = Hints.TryGetValue(name, out var hint) ? hint : "Review, then move or delete. Do not leave Takeout sitting on an unencrypted disk."
        };

    private static long DirSize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }
}
