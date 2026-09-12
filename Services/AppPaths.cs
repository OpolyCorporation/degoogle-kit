using System.IO;

namespace DeGoogleKit.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeGoogleKit");

    public static string Consent => Path.Combine(Root, "consent.json");
    public static string License => Path.Combine(Root, "license.json");
    public static string Audit => Path.Combine(Root, "audit.json");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string DnsBackup => Path.Combine(Root, "dns-backup.json");
    public static string Chat => Path.Combine(Root, "coach-chat.json");
    public static string Plan => Path.Combine(Root, "plan.json");
    public static string Normalized => Path.Combine(Root, "normalized");
    public static string UpdateState => Path.Combine(Root, "update.json");
    public static string UpdateCache => Path.Combine(Root, "update");
    public static string Permissions => Path.Combine(Root, "permissions.json");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
