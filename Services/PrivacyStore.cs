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
    public string SupabaseUrl { get; set; } = "";
    public string SupabaseAnonKey { get; set; } = "";
}

public static class PrivacyStore
{
    public const int CurrentNoticeVersion = LegalCopy.NoticeVersion;

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
        using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
        {
            foreach (var path in Directory.EnumerateFiles(AppPaths.Root, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith("account.bin", StringComparison.OrdinalIgnoreCase))
                    continue;
                var entry = Path.GetRelativePath(AppPaths.Root, path).Replace('\\', '/');
                zip.CreateEntryFromFile(path, entry, CompressionLevel.Fastest);
            }
        }
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
        sb.AppendLine("Controller (optional account, payments, this website): " + LegalCopy.IdentityLine);
        sb.AppendLine("Local files on this PC: you control the copy in AppData until you choose an optional cloud feature.");
        sb.AppendLine("Processors: Supabase (optional account), Stripe (payments), GitHub/Microsoft (downloads and updates). Optional Cloud AI = the provider you pick, never Google.");
        sb.AppendLine("Public notice: " + LegalCopy.PrivacyUrl);
        sb.AppendLine("Terms: " + LegalCopy.TermsUrl);
        sb.AppendLine("Complaints: Datatilsynet — " + LegalCopy.DatatilsynetUrl);
        sb.AppendLine();
        sb.AppendLine($"Storage folder: {AppPaths.Root}");
        sb.AppendLine("consent.json — GDPR consent and notice version (Art. 7).");
        sb.AppendLine("progress.json — which guide steps you marked done.");
        sb.AppendLine("plan.json — your migration plan (mode, destinations, service status).");
        sb.AppendLine("license.json — trial/Pro status. No payment card data.");
        sb.AppendLine("account.bin — optional Supabase session (DPAPI). Not included in the Desktop export zip.");
        sb.AppendLine("account-cloud.json — copy of plan/checklist pulled from your account when you export while signed in.");
        sb.AppendLine("audit.json — local action log (uninstall started, DNS changed, exports).");
        sb.AppendLine("settings.json — AI provider choice, DPAPI-protected API key, optional Supabase project URL.");
        sb.AppendLine("permissions.json — what you allowed (scan, links, clipboard, updates, Takeout, desktop files, secrets, account).");
        sb.AppendLine("dns-backup.json — previous DNS so a change can be reversed.");
        sb.AppendLine("normalized/ — local Takeout conversions (maps, Keep notes, YouTube OPML). Never uploaded.");
        sb.AppendLine();
        sb.AppendLine("We do not sell data, run ads, or send telemetry. No Google APIs.");
        sb.AppendLine("Check for updates (optional, you click it) downloads a version file from our GitHub release feed — not Google. It sends the app version in the User-Agent. You can change the feed URL.");
        sb.AppendLine("An optional free account uses Supabase Auth (email/password, not Google) to back up your plan and checklist. API keys, Takeout files, and scan results stay on this PC. While you are signed in, the app may ping that project about once a day so a free-plan database is less likely to pause (a timestamp only, not your plan).");
        sb.AppendLine("Legal bases: consent (Art. 6(1)(a)) for the optional account and for Cloud AI; contract (Art. 6(1)(b)) for local features and paid licenses; legal obligation (Art. 6(1)(c)) for paid-order records.");
        sb.AppendLine("Retention: local until you delete or uninstall; account until you delete the account.");
        sb.AppendLine("Transfers: none in local mode. Account and payments use the processors named above (SCCs/adequacy if outside the EEA).");
        return sb.ToString();
    }
}
