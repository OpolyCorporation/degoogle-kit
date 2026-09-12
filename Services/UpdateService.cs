using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace DeGoogleKit.Services;

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ReleasedAt { get; set; } = "";
}

public sealed class UpdateState
{
    public string? PendingVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Sha256 { get; set; }
    public string? Changelog { get; set; }
    public string? PackagePath { get; set; }
    public DateTime? ApplyAt { get; set; }
    public bool RemindOnLaunch { get; set; }
    public UpdateManifest? LastManifest { get; set; }
}

public sealed class UpdateCheckResult
{
    public bool ReachedServer { get; init; }
    public string Message { get; init; } = "";
    public UpdateManifest? Manifest { get; init; }
    public bool IsNewer { get; init; }
}

public static class UpdateService
{
    public const string DefaultFeedUrl =
        "https://github.com/OpolyCorporation/degoogle-kit/releases/latest/download/latest.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string FeedUrl
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("DGK_UPDATE_FEED")?.Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env;
            var custom = PrivacyStore.Settings.UpdateFeedUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            var beside = Path.Combine(AppContext.BaseDirectory, "updates", "feed.url");
            if (File.Exists(beside))
            {
                var line = File.ReadAllText(beside).Trim();
                if (line.Length > 0) return line;
            }
            return DefaultFeedUrl;
        }
    }

    public static UpdateState State => JsonFile.Load(AppPaths.UpdateState, new UpdateState());

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            string body;
            if (TryReadLocalFeed(FeedUrl, out var local))
            {
                body = local;
            }
            else
            {
                using var http = CreateClient();
                using var res = await http.GetAsync(FeedUrl, ct);
                body = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode)
                {
                    return new UpdateCheckResult
                    {
                        ReachedServer = false,
                        Message = res.StatusCode == System.Net.HttpStatusCode.NotFound
                            ? "No cloud release is published yet. When a GitHub release includes latest.json, Check for updates will find it."
                            : $"Update server returned {(int)res.StatusCode}."
                    };
                }
            }

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(body, Json);
            if (manifest is null || !TryParseVersion(manifest.Version, out var remote))
            {
                return new UpdateCheckResult { ReachedServer = true, Message = "The update file was not valid." };
            }

            var newer = remote > AppInfo.Version;
            var state = State;
            state.LastManifest = manifest;
            if (!newer)
            {
                state.RemindOnLaunch = false;
                state.ApplyAt = null;
            }
            Save(state);
            PrivacyStore.Log("update_checked", manifest.Version);

            return new UpdateCheckResult
            {
                ReachedServer = true,
                Manifest = manifest,
                IsNewer = newer,
                Message = newer
                    ? $"Version {manifest.Version} is available (you have {AppInfo.VersionText})."
                    : $"You are on {AppInfo.VersionText}. That is the latest."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new UpdateCheckResult
            {
                ReachedServer = false,
                Message = "Could not reach the update feed. Check the URL in Privacy, or your network."
            };
        }
    }

    public static void ScheduleLater(UpdateManifest manifest)
    {
        var state = FromManifest(manifest);
        state.RemindOnLaunch = true;
        state.ApplyAt = null;
        Save(state);
        PrivacyStore.Log("update_later", manifest.Version);
    }

    public static void ScheduleMidnight(UpdateManifest manifest, string? packagePath)
    {
        var state = FromManifest(manifest);
        state.RemindOnLaunch = false;
        state.PackagePath = packagePath;
        state.ApplyAt = DateTime.Today.AddDays(1);
        Save(state);
        PrivacyStore.Log("update_midnight", $"{manifest.Version} at {state.ApplyAt:yyyy-MM-dd HH:mm}");
    }

    public static void SnoozeDueUpdate()
    {
        var state = State;
        state.ApplyAt = null;
        state.RemindOnLaunch = state.LastManifest is not null;
        Save(state);
        PrivacyStore.Log("update_snoozed", state.PendingVersion ?? "");
    }

    public static bool ShouldRemindOnLaunch()
    {
        var s = State;
        return s.RemindOnLaunch && s.LastManifest is not null && IsNewer(s.LastManifest);
    }

    public static bool IsScheduledUpdateDue()
    {
        var s = State;
        return s.ApplyAt is { } at && at <= DateTime.Now && s.LastManifest is not null && IsNewer(s.LastManifest);
    }

    public static async Task<string> DownloadAsync(UpdateManifest manifest, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            throw new InvalidOperationException("This release has no download URL yet.");
        if (!manifest.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !manifest.DownloadUrl.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !manifest.DownloadUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Updates must be downloaded over HTTPS.");

        Directory.CreateDirectory(AppPaths.UpdateCache);
        var zipPath = Path.Combine(AppPaths.UpdateCache, $"DeGoogleKit-{Sanitize(manifest.Version)}.zip");
        using var http = CreateClient();
        using var res = await http.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? 0;
        if (total > 500L * 1024 * 1024)
            throw new InvalidOperationException("The update package is larger than 500 MB. Refusing to download.");

        await using (var input = await res.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(zipPath))
        {
            var buffer = new byte[64 * 1024];
            long read = 0;
            int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report(100.0 * read / total);
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath, ct)));
            if (!hash.Equals(manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zipPath);
                throw new InvalidOperationException("The download did not match the signed checksum. Update cancelled.");
            }
        }

        progress?.Report(100);
        var state = State;
        state.PackagePath = zipPath;
        Save(state);
        PrivacyStore.Log("update_downloaded", manifest.Version);
        return zipPath;
    }

    public static void ApplyAndRestart(UpdateManifest manifest, string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("The update package is missing. Check for updates again.", zipPath);

        var staging = Path.Combine(AppPaths.UpdateCache, "staging");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        ExtractSafe(zipPath, staging);

        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "DeGoogleKit.exe");
        var scriptPath = Path.Combine(AppPaths.UpdateCache, "apply.ps1");
        var installLit = PsLiteral(installDir);
        var stagingLit = PsLiteral(staging);
        var exeLit = PsLiteral(exeName);
        File.WriteAllText(scriptPath,
            "$ErrorActionPreference = 'Continue'\r\n" +
            "$install = " + installLit + "\r\n" +
            "$staging = " + stagingLit + "\r\n" +
            "$exe = " + exeLit + "\r\n" +
            "Start-Sleep -Seconds 2\r\n" +
            "$deadline = (Get-Date).AddMinutes(2)\r\n" +
            "while (Get-Process -Name 'DeGoogleKit' -ErrorAction SilentlyContinue) {\r\n" +
            "  if ((Get-Date) -gt $deadline) { break }\r\n" +
            "  Start-Sleep -Seconds 1\r\n" +
            "}\r\n" +
            "robocopy $staging $install /E /IS /IT /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null\r\n" +
            "$target = Join-Path $install $exe\r\n" +
            "if (Test-Path $target) { Start-Process $target }\r\n",
            Encoding.UTF8);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        var state = State;
        state.RemindOnLaunch = false;
        state.ApplyAt = null;
        Save(state);
        PrivacyStore.Log("update_apply", manifest.Version);
        Application.Current.Shutdown();
    }

    public static async Task<bool> ApplyDueAsync(Window owner)
    {
        if (!IsScheduledUpdateDue()) return false;
        var state = State;
        var manifest = state.LastManifest!;
        try
        {
            owner.IsEnabled = false;
            var zip = state.PackagePath;
            if (string.IsNullOrWhiteSpace(zip) || !File.Exists(zip))
                zip = await DownloadAsync(manifest, null);
            ApplyAndRestart(manifest, zip);
            return true;
        }
        catch (Exception ex)
        {
            owner.IsEnabled = true;
            MessageBox.Show(
                "The scheduled midnight update could not be installed:\n" + ex.Message,
                "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static UpdateState FromManifest(UpdateManifest manifest)
    {
        var state = State;
        state.PendingVersion = manifest.Version;
        state.DownloadUrl = manifest.DownloadUrl;
        state.Sha256 = manifest.Sha256;
        state.Changelog = manifest.Changelog;
        state.LastManifest = manifest;
        return state;
    }

    private static void Save(UpdateState state) => JsonFile.Save(AppPaths.UpdateState, state);

    private static bool IsNewer(UpdateManifest manifest) =>
        TryParseVersion(manifest.Version, out var remote) && remote > AppInfo.Version;

    internal static bool TryParseVersion(string? version, out Version parsed)
    {
        parsed = new Version(0, 0, 0, 0);
        var parts = (version ?? "").Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        var build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
        var rev = parts.Length > 3 && int.TryParse(parts[3], out var r) ? r : 0;
        parsed = new Version(major, minor, build, rev);
        return true;
    }

    private static bool TryReadLocalFeed(string url, out string body)
    {
        body = "";
        string? path = null;
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            path = Uri.UnescapeDataString(uri.LocalPath);
        else if (url.Length > 2 && url[1] == ':' && url.Contains(".json", StringComparison.OrdinalIgnoreCase))
            path = url;
        if (path is null || !File.Exists(path)) return false;
        body = File.ReadAllText(path);
        return true;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DeGoogleKit/" + AppInfo.VersionText);
        return http;
    }

    private static string Sanitize(string version)
    {
        var chars = version.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_').ToArray();
        return new string(chars);
    }

    private static string PsLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    private static void ExtractSafe(string zipPath, string dest)
    {
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/')) continue;
            var target = Path.GetFullPath(Path.Combine(dest, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The update zip contained an unsafe path.");
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (entry.Name.Length == 0) continue;
            entry.ExtractToFile(target, true);
        }

        FlattenIfNested(dest);
    }

    private static void FlattenIfNested(string dest)
    {
        var entries = Directory.GetFileSystemEntries(dest);
        if (entries.Length != 1 || !Directory.Exists(entries[0])) return;
        if (!File.Exists(Path.Combine(entries[0], "DeGoogleKit.exe"))) return;
        var nested = entries[0];
        foreach (var item in Directory.GetFileSystemEntries(nested))
        {
            var name = Path.GetFileName(item);
            var target = Path.Combine(dest, name);
            if (Directory.Exists(item))
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(item, target);
            }
            else
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(item, target);
            }
        }
        Directory.Delete(nested, true);
    }
}
