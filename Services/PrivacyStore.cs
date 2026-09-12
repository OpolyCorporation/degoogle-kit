using System.IO;
using System.IO.Compression;
using System.Text;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public sealed class ConsentRecord
{
    public int NoticeVersion { get; set; }
    public DateTime AcceptedAt { get; set; }
    public bool AgeConfirmed { get; set; }
    public bool PrivacyAccepted { get; set; }
    public bool LocalStorageAccepted { get; set; }
    public bool CloudAiConsent { get; set; }
}

public sealed class AppSettings
{
    public string AiProvider { get; set; } = "groq";
    public string AiModel { get; set; } = "llama-3.3-70b-versatile";
    public string AiBaseUrl { get; set; } = "";
    public string ProtectedApiKey { get; set; } = "";
    public string UpdateFeedUrl { get; set; } = "";
    public string LicenseApiUrl { get; set; } = "";
}

public static class PrivacyStore
{
    public const int CurrentNoticeVersion = 1;

    public static ConsentRecord Consent => JsonFile.Load(AppPaths.Consent, new ConsentRecord());
    public static AppSettings Settings => JsonFile.Load(AppPaths.Settings, new AppSettings());

    public static bool HasValidConsent()
    {
        var c = Consent;
        return c.NoticeVersion == CurrentNoticeVersion
               && c.AgeConfirmed
               && c.PrivacyAccepted
               && c.LocalStorageAccepted;
    }

    public static void SaveConsent(ConsentRecord record)
    {
        record.NoticeVersion = CurrentNoticeVersion;
        record.AcceptedAt = DateTime.Now;
        JsonFile.Save(AppPaths.Consent, record);
        Log("consent_saved", "Privacy notice v" + CurrentNoticeVersion);
    }

    public static void SaveSettings(AppSettings settings) => JsonFile.Save(AppPaths.Settings, settings);

    public static void SetCloudAiConsent(bool value)
    {
        var c = Consent;
        c.CloudAiConsent = value;
        JsonFile.Save(AppPaths.Consent, c);
        Log("cloud_ai_consent", value ? "granted" : "withdrawn");
    }

    public static List<AuditEvent> Audit() => JsonFile.Load(AppPaths.Audit, new List<AuditEvent>());

    public static void Log(string action, string detail = "")
    {
        var events = Audit();
        events.Add(new AuditEvent { Action = action, Detail = detail });
        if (events.Count > 400) events.RemoveRange(0, events.Count - 400);
        JsonFile.Save(AppPaths.Audit, events);
    }

    public static string ExportArchive()
    {
        AppPaths.EnsureRoot();
        var dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"degoogle-kit-export-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        if (File.Exists(dest)) File.Delete(dest);
        ZipFile.CreateFromDirectory(AppPaths.Root, dest, CompressionLevel.Fastest, false);
        Log("export", dest);
        return dest;
    }

    public static void DeleteAllLocalData()
    {
        Log("delete_requested", "user");
        if (Directory.Exists(AppPaths.Root))
        {
            Directory.Delete(AppPaths.Root, true);
        }
    }

    public static string DataMap()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Controller: you (this app runs only on your PC).");
        sb.AppendLine("Processor: none, unless you turn on Cloud AI with your own API key.");
        sb.AppendLine();
        sb.AppendLine($"Storage folder: {AppPaths.Root}");
        sb.AppendLine("consent.json — GDPR consent and notice version (Art. 7).");
        sb.AppendLine("progress.json — which guide steps you marked done.");
        sb.AppendLine("license.json — trial/Pro status. No payment card data.");
        sb.AppendLine("audit.json — local action log (uninstall started, DNS changed, exports).");
        sb.AppendLine("settings.json — AI provider choice and DPAPI-protected API key.");
        sb.AppendLine("dns-backup.json — previous DNS so a change can be reversed.");
        sb.AppendLine("normalized/ — local Takeout conversions (maps, Keep notes, YouTube OPML). Never uploaded.");
        sb.AppendLine("permissions.json — what you allowed (scan, links, clipboard, updates, Takeout, desktop files, secrets).");
        sb.AppendLine();
        sb.AppendLine("We do not sell data, run ads, or send telemetry. No Google APIs.");
        sb.AppendLine("Check for updates (optional, you click it) downloads a version file from our GitHub release feed — not Google. It sends the app version in the User-Agent. You can change the feed URL.");
        sb.AppendLine("Legal bases: consent (Art. 6(1)(a)) for optional Cloud AI; legitimate interest / contract (Art. 6(1)(b)) for local features you ask the app to run.");
        sb.AppendLine("Retention: until you delete the data or uninstall. You can export or erase anytime.");
        sb.AppendLine("Transfers: none by default. Cloud AI leaves the device only after separate consent, to the provider you choose (not Google).");
        sb.AppendLine("Complaints (Denmark): Datatilsynet — https://www.datatilsynet.dk/");
        return sb.ToString();
    }
}
