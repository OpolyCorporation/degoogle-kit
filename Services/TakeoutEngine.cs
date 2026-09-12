using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class TakeoutEngine
{
    private static readonly HashSet<string> PhotoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".dng" };
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".m4v", ".webm", ".mkv", ".avi" };

    public static TakeoutInventory Analyze(string path)
    {
        if (Directory.Exists(path)) return FromFolder(path);
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return FromZip(path);
        throw new InvalidOperationException("Drop a Google Takeout folder or .zip.");
    }

    public static string Normalize(string path, TakeoutInventory inventory)
    {
        Directory.CreateDirectory(AppPaths.Normalized);
        var mapsDir = Path.Combine(AppPaths.Normalized, "locations");
        var notesDir = Path.Combine(AppPaths.Normalized, "notes");
        var ytDir = Path.Combine(AppPaths.Normalized, "youtube");
        var pwdDir = Path.Combine(AppPaths.Normalized, "passwords");
        Directory.CreateDirectory(mapsDir);
        Directory.CreateDirectory(notesDir);
        Directory.CreateDirectory(ytDir);
        Directory.CreateDirectory(pwdDir);

        if (Directory.Exists(path))
        {
            ConvertKeepFromFolder(path, notesDir);
            ConvertMapsFromFolder(path, mapsDir);
            ConvertYoutubeFromFolder(path, ytDir);
            CopyPasswordCsvFromFolder(path, pwdDir);
        }
        else
        {
            using var zip = ZipFile.OpenRead(path);
            ConvertKeepFromZip(zip, notesDir);
            ConvertMapsFromZip(zip, mapsDir);
            ConvertYoutubeFromZip(zip, ytDir);
            CopyPasswordCsvFromZip(zip, pwdDir);
        }

        var manifest = new
        {
            generated = DateTime.UtcNow,
            source = path,
            inventory,
            note = "Processed locally. Nothing was uploaded. Do not delete Google until destination counts match."
        };
        File.WriteAllText(Path.Combine(AppPaths.Normalized, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonFile.Options));
        PrivacyStore.Log("takeout_normalized", AppPaths.Normalized);
        return AppPaths.Normalized;
    }

    private static TakeoutInventory FromFolder(string path)
    {
        var root = FindTakeoutRoot(path);
        var inv = new TakeoutInventory { SourcePath = root };
        foreach (var file in EnumerateFilesSafe(root))
        {
            Consume(inv, Rel(root, file), file, () => File.OpenRead(file), new FileInfo(file).Length);
        }
        Finish(inv);
        return inv;
    }

    private static TakeoutInventory FromZip(string path)
    {
        var inv = new TakeoutInventory { SourcePath = path };
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName) || entry.FullName.EndsWith('/')) continue;
            var rel = StripTakeout(entry.FullName);
            Consume(inv, rel, entry.FullName, entry.Open, entry.Length);
        }
        Finish(inv);
        return inv;
    }

    private static void Consume(TakeoutInventory inv, string rel, string _, Func<Stream> open, long length)
    {
        var n = "/" + rel.Replace('\\', '/').TrimStart('/');
        var ext = Path.GetExtension(n);

        if (n.Contains("/Mail/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".mbox", StringComparison.OrdinalIgnoreCase))
        {
            inv.Emails += CountToken(open, "\nFrom ");
            Mark(inv, "gmail");
        }
        else if (n.Contains("/Contacts/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase))
        {
            inv.Contacts += CountToken(open, "BEGIN:VCARD");
            Mark(inv, "contacts");
        }
        else if (n.Contains("/Calendar/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
        {
            inv.Events += CountToken(open, "BEGIN:VEVENT");
            Mark(inv, "calendar");
        }
        else if (n.Contains("/Keep/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            inv.KeepNotes++;
            Mark(inv, "keep");
        }
        else if (n.Contains("Google Photos", StringComparison.OrdinalIgnoreCase) || n.Contains("/Photos/", StringComparison.OrdinalIgnoreCase))
        {
            if (PhotoExt.Contains(ext)) { inv.Photos++; Mark(inv, "photos"); }
            if (VideoExt.Contains(ext)) { inv.Videos++; Mark(inv, "photos"); }
        }
        else if (n.Contains("/Drive/", StringComparison.OrdinalIgnoreCase))
        {
            inv.DriveBytes += length;
            Mark(inv, "drive");
            if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase)) Mark(inv, "docs");
            if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                Mark(inv, "sheets");
        }
        else if (n.Contains("/Maps/", StringComparison.OrdinalIgnoreCase) || n.Contains("Saved Places", StringComparison.OrdinalIgnoreCase))
        {
            Mark(inv, "maps");
            if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && length < 40_000_000)
                inv.SavedPlaces += CountPlaces(open);
        }
        else if (n.Contains("Location History", StringComparison.OrdinalIgnoreCase))
        {
            inv.HasLocationHistory = true;
        }
        else if (n.Contains("YouTube", StringComparison.OrdinalIgnoreCase))
        {
            Mark(inv, "youtube");
            if (n.Contains("subscription", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                inv.YoutubeSubscriptions += Math.Max(0, CountLines(open) - 1);
            if (n.Contains("playlist", StringComparison.OrdinalIgnoreCase))
            {
                inv.YoutubePlaylists++;
                Mark(inv, "ytmusic");
            }
        }
        else if (n.EndsWith("Passwords.csv", StringComparison.OrdinalIgnoreCase) || n.Contains("/Chrome/", StringComparison.OrdinalIgnoreCase))
        {
            Mark(inv, "chrome");
            if (n.EndsWith("Passwords.csv", StringComparison.OrdinalIgnoreCase))
            {
                inv.HasPasswordCsv = true;
                Mark(inv, "passwords");
            }
            if (n.Contains("Bookmark", StringComparison.OrdinalIgnoreCase)) inv.HasChromeBookmarks = true;
        }
    }

    private static void Finish(TakeoutInventory inv)
    {
        var bits = new List<string>();
        if (inv.Emails > 0) bits.Add($"{inv.Emails:N0} emails");
        if (inv.Contacts > 0) bits.Add($"{inv.Contacts:N0} contacts");
        if (inv.Events > 0) bits.Add($"{inv.Events:N0} calendar events");
        if (inv.DriveBytes > 0) bits.Add($"{inv.DriveBytes / 1024d / 1024d / 1024d:0.0} GB Drive");
        if (inv.Photos > 0) bits.Add($"{inv.Photos:N0} photos");
        if (inv.Videos > 0) bits.Add($"{inv.Videos:N0} videos");
        if (inv.KeepNotes > 0) bits.Add($"{inv.KeepNotes:N0} Keep notes");
        if (inv.SavedPlaces > 0) bits.Add($"{inv.SavedPlaces:N0} saved places");
        if (inv.YoutubeSubscriptions > 0) bits.Add($"{inv.YoutubeSubscriptions:N0} YouTube subscriptions");
        if (inv.YoutubePlaylists > 0) bits.Add($"{inv.YoutubePlaylists:N0} playlists");
        if (inv.HasPasswordCsv) bits.Add("Chrome password CSV (plaintext — delete after import)");
        if (inv.HasLocationHistory) bits.Add("Location History present (kept local, not uploaded)");
        inv.Summary = bits.Count == 0
            ? "This looks like a Takeout archive, but we did not recognise the usual Gmail/Photos/Drive folders. You can still keep it as a backup."
            : string.Join("\n", bits.Select(b => "✓ " + b));
    }

    private static void ConvertKeepFromFolder(string root, string dest)
    {
        foreach (var file in EnumerateFilesSafe(FindTakeoutRoot(root)).Where(f => Rel(root, f).Contains("/Keep/", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            WriteKeepNote(File.ReadAllText(file), dest, Path.GetFileNameWithoutExtension(file));
    }

    private static void ConvertKeepFromZip(ZipArchive zip, string dest)
    {
        foreach (var entry in zip.Entries)
        {
            var rel = StripTakeout(entry.FullName);
            if (!rel.Contains("/Keep/", StringComparison.OrdinalIgnoreCase) || !rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = entry.Open();
            using var r = new StreamReader(s);
            WriteKeepNote(r.ReadToEnd(), dest, Path.GetFileNameWithoutExtension(entry.Name));
        }
    }

    private static void WriteKeepNote(string json, string dest, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var t = doc.RootElement;
            var title = t.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : fallback;
            var body = t.TryGetProperty("textContent", out var bodyEl) ? bodyEl.GetString() : json;
            var name = Sanitize(string.IsNullOrWhiteSpace(title) ? fallback : title!) + ".md";
            File.WriteAllText(Path.Combine(dest, name), $"# {title}\n\n{body}\n");
        }
        catch
        {
            File.WriteAllText(Path.Combine(dest, Sanitize(fallback) + ".md"), json);
        }
    }

    private static void ConvertMapsFromFolder(string root, string dest)
    {
        foreach (var file in EnumerateFilesSafe(FindTakeoutRoot(root)))
        {
            var rel = Rel(root, file);
            if (!rel.Contains("/Maps/", StringComparison.OrdinalIgnoreCase) && !rel.Contains("Saved Places", StringComparison.OrdinalIgnoreCase)) continue;
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)) continue;
            if (new FileInfo(file).Length > 40_000_000) continue;
            WriteMapOutputs(File.ReadAllText(file), dest);
        }
    }

    private static void ConvertMapsFromZip(ZipArchive zip, string dest)
    {
        foreach (var entry in zip.Entries)
        {
            var rel = StripTakeout(entry.FullName);
            if (!rel.Contains("/Maps/", StringComparison.OrdinalIgnoreCase) && !rel.Contains("Saved Places", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Length > 40_000_000) continue;
            if (!rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !rel.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = entry.Open();
            using var r = new StreamReader(s);
            WriteMapOutputs(r.ReadToEnd(), dest);
        }
    }

    private static void WriteMapOutputs(string raw, string dest)
    {
        var places = ParsePlaces(raw);
        if (places.Count == 0) return;
        File.WriteAllText(Path.Combine(dest, "places.csv"),
            "name,lat,lng,address,category\n" +
            string.Join('\n', places.Select(p => $"{Csv(p.Name)},{p.Lat.ToString(CultureInfo.InvariantCulture)},{p.Lng.ToString(CultureInfo.InvariantCulture)},{Csv(p.Address)},{Csv(p.Category)}")));
        File.WriteAllText(Path.Combine(dest, "places.geojson"), ToGeoJson(places));
        File.WriteAllText(Path.Combine(dest, "places.gpx"), ToGpx(places));
        File.WriteAllText(Path.Combine(dest, "places.kml"), ToKml(places));
    }

    private static void ConvertYoutubeFromFolder(string root, string dest)
    {
        foreach (var file in EnumerateFilesSafe(FindTakeoutRoot(root)))
        {
            if (!Rel(root, file).Contains("subscription", StringComparison.OrdinalIgnoreCase) || !file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
            WriteOpml(File.ReadAllLines(file), dest);
        }
    }

    private static void ConvertYoutubeFromZip(ZipArchive zip, string dest)
    {
        foreach (var entry in zip.Entries)
        {
            var rel = StripTakeout(entry.FullName);
            if (!rel.Contains("subscription", StringComparison.OrdinalIgnoreCase) || !rel.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = entry.Open();
            using var r = new StreamReader(s);
            WriteOpml(r.ReadToEnd().Split('\n'), dest);
        }
    }

    private static void WriteOpml(IEnumerable<string> lines, string dest)
    {
        var outlines = new List<XElement>();
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            var url = parts[1].Trim().Trim('"');
            var title = parts[^1].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(url)) continue;
            outlines.Add(new XElement("outline",
                new XAttribute("type", "rss"),
                new XAttribute("text", title),
                new XAttribute("xmlUrl", url),
                new XAttribute("htmlUrl", url)));
        }
        var opml = new XDocument(new XElement("opml", new XAttribute("version", "1.0"),
            new XElement("head", new XElement("title", "YouTube subscriptions from DeGoogle Kit")),
            new XElement("body", outlines)));
        opml.Save(Path.Combine(dest, "subscriptions.opml"));
        File.WriteAllText(Path.Combine(dest, "README.txt"),
            "FreeTube/NewPipe still use YouTube as the backend. This OPML is an archive + import helper, not a Google-free YouTube.");
    }

    private static void CopyPasswordCsvFromFolder(string root, string dest)
    {
        foreach (var file in EnumerateFilesSafe(FindTakeoutRoot(root)))
        {
            if (!file.EndsWith("Passwords.csv", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(dest, "passwords.csv"), true);
            WritePasswordWarning(dest);
        }
    }

    private static void CopyPasswordCsvFromZip(ZipArchive zip, string dest)
    {
        foreach (var entry in zip.Entries)
        {
            if (!entry.Name.Equals("Passwords.csv", StringComparison.OrdinalIgnoreCase)) continue;
            entry.ExtractToFile(Path.Combine(dest, "passwords.csv"), true);
            WritePasswordWarning(dest);
        }
    }

    private static void WritePasswordWarning(string dest) =>
        File.WriteAllText(Path.Combine(dest, "DELETE-ME.txt"),
            "This CSV contains passwords in readable form.\nImport into Proton Pass or Bitwarden, then delete this folder.\nDeGoogle Kit never uploads it.");

    private static int CountPlaces(Func<Stream> open)
    {
        try
        {
            using var s = open();
            using var r = new StreamReader(s);
            return ParsePlaces(r.ReadToEnd()).Count;
        }
        catch { return 0; }
    }

    private sealed record Place(string Name, double Lat, double Lng, string Address, string Category);

    private static List<Place> ParsePlaces(string raw)
    {
        var list = new List<Place>();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in features.EnumerateArray())
                {
                    var props = f.TryGetProperty("properties", out var p) ? p : default;
                    var geom = f.TryGetProperty("geometry", out var g) ? g : default;
                    double lat = 0, lng = 0;
                    if (geom.ValueKind == JsonValueKind.Object && geom.TryGetProperty("coordinates", out var c) && c.GetArrayLength() >= 2)
                    {
                        lng = c[0].GetDouble();
                        lat = c[1].GetDouble();
                    }
                    var name = Props(props, "Title", "name", "location", "label");
                    var address = Props(props, "address", "Address", "location");
                    var cat = Props(props, "Category", "list", "type");
                    if (lat != 0 || lng != 0) list.Add(new Place(name, lat, lng, address, cat));
                }
            }
        }
        catch { /* KML or unknown JSON is ignored for counts */ }
        return list;
    }

    private static string Props(JsonElement props, params string[] keys)
    {
        if (props.ValueKind != JsonValueKind.Object) return "";
        foreach (var key in keys)
            if (props.TryGetProperty(key, out var v)) return v.ToString();
        return "";
    }

    private static string ToGeoJson(List<Place> places) =>
        JsonSerializer.Serialize(new
        {
            type = "FeatureCollection",
            features = places.Select(p => new
            {
                type = "Feature",
                geometry = new { type = "Point", coordinates = new[] { p.Lng, p.Lat } },
                properties = new { name = p.Name, address = p.Address, category = p.Category }
            })
        }, JsonFile.Options);

    private static string ToGpx(List<Place> places)
    {
        var wpts = string.Join('\n', places.Select(p =>
            $"<wpt lat=\"{p.Lat.ToString(CultureInfo.InvariantCulture)}\" lon=\"{p.Lng.ToString(CultureInfo.InvariantCulture)}\"><name>{System.Net.WebUtility.HtmlEncode(p.Name)}</name></wpt>"));
        return $"<?xml version=\"1.0\"?><gpx version=\"1.1\" creator=\"DeGoogle Kit\">{wpts}</gpx>";
    }

    private static string ToKml(List<Place> places)
    {
        var pm = string.Join('\n', places.Select(p =>
            $"<Placemark><name>{System.Net.WebUtility.HtmlEncode(p.Name)}</name><Point><coordinates>{p.Lng.ToString(CultureInfo.InvariantCulture)},{p.Lat.ToString(CultureInfo.InvariantCulture)},0</coordinates></Point></Placemark>"));
        return $"<?xml version=\"1.0\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>{pm}</Document></kml>";
    }

    private static long CountToken(Func<Stream> open, string token)
    {
        try
        {
            using var s = open();
            using var r = new StreamReader(s);
            long n = 0;
            var first = true;
            string? line;
            while ((line = r.ReadLine()) is not null)
            {
                if (token == "\nFrom ")
                {
                    if (line.StartsWith("From ")) n++;
                }
                else if (line.Contains(token, StringComparison.Ordinal)) n++;
                first = false;
            }
            if (first) { /* empty */ }
            return n;
        }
        catch { return 0; }
    }

    private static int CountLines(Func<Stream> open)
    {
        try
        {
            using var s = open();
            using var r = new StreamReader(s);
            var n = 0;
            while (r.ReadLine() is not null) n++;
            return n;
        }
        catch { return 0; }
    }

    private static void Mark(TakeoutInventory inv, string id)
    {
        if (!inv.DetectedServiceIds.Contains(id)) inv.DetectedServiceIds.Add(id);
    }

    private static string StripTakeout(string full) =>
        "/" + full.Replace('\\', '/').TrimStart('/');

    private static string FindTakeoutRoot(string path)
    {
        var takeout = Path.Combine(path, "Takeout");
        return Directory.Exists(takeout) ? takeout : path;
    }

    private static string Rel(string root, string file) =>
        "Takeout/" + Path.GetRelativePath(FindTakeoutRoot(root), file).Replace('\\', '/');

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories); }
        catch { yield break; }
        foreach (var f in files)
            yield return f;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "note" : name.Trim();
    }

    private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
