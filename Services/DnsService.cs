using System.Diagnostics;
using System.Net.NetworkInformation;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public sealed class DnsBackup
{
    public string Adapter { get; set; } = "";
    public List<string> Servers { get; set; } = [];
}

public enum DnsPreset
{
    Quad9,
    Cloudflare,
    AdGuard
}

public static class DnsService
{
    private static readonly HashSet<string> GoogleDns = new(StringComparer.OrdinalIgnoreCase)
    {
        "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"
    };

    public static (string Label, string Primary, string Secondary) Describe(DnsPreset preset) =>
        preset switch
        {
            DnsPreset.Cloudflare => ("Cloudflare 1.1.1.1", "1.1.1.1", "1.0.0.1"),
            DnsPreset.AdGuard => ("AdGuard DNS", "94.140.14.14", "94.140.15.15"),
            _ => ("Quad9", "9.9.9.9", "149.112.112.112")
        };

    public static (List<string> Servers, bool LooksLikeGoogle, string Adapter) Detect()
    {
        var servers = new List<string>();
        var adapter = "";
        var google = false;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var dns = nic.GetIPProperties().DnsAddresses;
            if (dns.Count == 0) continue;
            if (string.IsNullOrEmpty(adapter)) adapter = nic.Name;
            var ips = dns.Select(ip => ip.ToString()).ToList();
            if (ips.Any(s => GoogleDns.Contains(s))) google = true;
            servers.Add(nic.Name + ": " + string.Join(", ", ips));
        }

        return (servers, google, adapter);
    }

    public static void AttachToScan(ScanSnapshot snap)
    {
        var (servers, google, _) = Detect();
        snap.DnsServers.AddRange(servers);
        snap.DnsLooksLikeGoogle = google;
    }

    public static (bool Ok, string Message) ApplyQuad9(string adapter) =>
        ApplyPreset(adapter, DnsPreset.Quad9);

    public static (bool Ok, string Message) ApplyPreset(string adapter, DnsPreset preset)
    {
        var (label, primary, secondary) = Describe(preset);
        var current = Detect();
        if (string.IsNullOrWhiteSpace(adapter)) adapter = current.Adapter;
        if (string.IsNullOrWhiteSpace(adapter))
            return (false, "No active network adapter with DNS was found.");
        var ips = current.Servers
            .Where(s => s.StartsWith(adapter + ":", StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s[(adapter.Length + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        if (ips.Count == 0)
            ips = current.Servers.SelectMany(s => s.Contains(':') ? s[(s.IndexOf(':') + 1)..].Split(',', StringSplitOptions.TrimEntries) : [s]).ToList();
        JsonFile.Save(AppPaths.DnsBackup, new DnsBackup { Adapter = adapter, Servers = ips.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList() });
        try
        {
            if (!RunElevatedNetsh(adapter, primary, secondary))
                return (false, "Windows did not apply DNS. If you cancelled UAC, nothing changed.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, "Administrator permission was not granted. DNS was not changed.");
        }

        PrivacyStore.Log("dns_changed", $"adapter={adapter} -> {label}");
        var after = Detect();
        if (!after.Servers.Any(s => s.Contains(primary, StringComparison.Ordinal)))
            return (false, label + " was requested but this PC still does not show " + primary + ". Check Settings → Network → DNS, then Scan again.");
        return (true, "This PC now uses " + label + " (" + primary + " / " + secondary + ") on " + adapter + ".");
    }

    public static (bool Ok, string Message) Restore()
    {
        var backup = JsonFile.Load(AppPaths.DnsBackup, (DnsBackup?)null);
        if (backup is null || string.IsNullOrWhiteSpace(backup.Adapter) || backup.Servers.Count == 0)
            return (false, "No DNS backup found yet. Apply a Pro DNS preset once first.");
        var primary = backup.Servers[0];
        var secondary = backup.Servers.Count > 1 ? backup.Servers[1] : null;
        try
        {
            if (!RunElevatedNetsh(backup.Adapter, primary, secondary))
                return (false, "Windows did not restore DNS. If you cancelled UAC, nothing changed.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, "Administrator permission was not granted. DNS was not restored.");
        }

        PrivacyStore.Log("dns_restored", backup.Adapter);
        var after = Detect();
        var restored = backup.Servers.Any(ip => after.Servers.Any(s => s.Contains(ip, StringComparison.OrdinalIgnoreCase)));
        if (!restored)
            return (false, "Restore was requested but this PC does not yet show the saved DNS (" + string.Join(", ", backup.Servers.Take(2)) + "). Check Settings → Network → DNS, then Scan again.");
        return (true, "Previous DNS was restored on " + backup.Adapter + ": " + string.Join(", ", backup.Servers.Take(3)) + ".");
    }

    private static bool RunElevatedNetsh(string adapter, string primary, string? secondary)
    {
        var escaped = adapter.Replace("'", "''");
        var script = $"Set-DnsClientServerAddress -InterfaceAlias '{escaped}' -ServerAddresses {primary}" +
                     (string.IsNullOrWhiteSpace(secondary) ? "" : $",{secondary}");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = true,
            Verb = "runas"
        };
        var proc = Process.Start(psi);
        if (proc is null) return false;
        proc.WaitForExit();
        return proc.ExitCode == 0;
    }
}
