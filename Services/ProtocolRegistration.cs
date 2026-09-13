using System.IO;
using Microsoft.Win32;

namespace DeGoogleKit.Services;

public static class ProtocolRegistration
{
    public const string Scheme = "degooglekit";
    public const string PendingFileName = "pending-activation.txt";

    public static string PendingPath
    {
        get
        {
            AppPaths.EnsureRoot();
            return Path.Combine(AppPaths.Root, PendingFileName);
        }
    }

    public static void EnsureCurrentUser()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return;
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Scheme);
            if (root is null) return;
            root.SetValue("", "URL:DeGoogle Kit");
            root.SetValue("URL Protocol", "");
            using var cmd = root.CreateSubKey(@"shell\open\command");
            cmd?.SetValue("", "\"" + exe + "\" \"%1\"");
        }
        catch
        {
            // Protocol is a convenience after Stripe; paste-to-activate still works.
        }
    }

    public static void WritePending(string payload)
    {
        payload = (payload ?? "").Trim();
        if (payload.Length == 0) return;
        AppPaths.EnsureRoot();
        File.WriteAllText(PendingPath, payload);
    }

    public static string? TakePending()
    {
        var path = PendingPath;
        if (!File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path).Trim();
            File.Delete(path);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    public static bool LooksLikePayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var t = raw.Trim();
        return t.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("cs_", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("DGK2.", StringComparison.Ordinal);
    }

    /// <summary>Returns a Stripe session id (cs_…) or a DGK2 key, or empty.</summary>
    public static string ExtractToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = raw.Trim().Trim('"');
        if (t.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
                return "";
            var query = uri.Query.TrimStart('?');
            if (query.Length == 0 && uri.PathAndQuery.Contains('?', StringComparison.Ordinal))
                query = uri.PathAndQuery[(uri.PathAndQuery.IndexOf('?') + 1)..];
            string session = "", key = "";
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq < 1) continue;
                var name = Uri.UnescapeDataString(part[..eq]);
                var val = Uri.UnescapeDataString(part[(eq + 1)..]);
                if (name.Equals("key", StringComparison.OrdinalIgnoreCase)) key = val;
                else if (name.Equals("session", StringComparison.OrdinalIgnoreCase)
                         || name.Equals("session_id", StringComparison.OrdinalIgnoreCase))
                    session = val;
            }
            if (key.StartsWith("DGK2.", StringComparison.Ordinal)) return key;
            if (session.StartsWith("cs_", StringComparison.OrdinalIgnoreCase)) return session;
            return "";
        }

        if (t.StartsWith("cs_", StringComparison.OrdinalIgnoreCase)) return t.Split()[0];
        if (t.StartsWith("DGK2.", StringComparison.Ordinal)) return t;
        return "";
    }
}
