using System.Net.NetworkInformation;
using System.Text.Json;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public sealed class DnsBackup
{
    public string Adapter { get; set; } = "";
    public List<string> Servers { get; set; } = [];
}

public static class DnsService
{
    private static readonly HashSet<string> GoogleDns = new(StringComparer.OrdinalIgnoreCase)
    {
        "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"
    };

    public static (List<string> Servers, bool LooksLikeGoogle, string Adapter) Detect()
    {
        var servers = new List<string>();
        var adapter = "";
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var dns = nic.GetIPProperties().DnsAddresses;
            if (dns.Count == 0) continue;
            adapter = nic.Name;
            foreach (var ip in dns)
            {
                var s = ip.ToString();
                if (!servers.Contains(s)) servers.Add(s);
            }
            break;
        }

        return (servers, servers.Any(s => GoogleDns.Contains(s)), adapter);
    }

    public static void AttachToScan(ScanSnapshot snap)
    {
        var (servers, google, _) = Detect();
        snap.DnsServers.AddRange(servers);
        snap.DnsLooksLikeGoogle = google;
    }

    public static string ApplyQuad9(string adapter)
    {
        if (string.IsNullOrWhiteSpace(adapter)) adapter = Detect().Adapter;
        var current = Detect();
        JsonFile.Save(AppPaths.DnsBackup, new DnsBackup { Adapter = adapter, Servers = current.Servers });
        RunElevatedNetsh(adapter, "9.9.9.9", "149.112.112.112");
        PrivacyStore.Log("dns_changed", $"adapter={adapter} -> Quad9");
        return adapter;
    }

    public static bool Restore()
    {
        var backup = JsonFile.Load(AppPaths.DnsBackup, (DnsBackup?)null);
        if (backup is null || string.IsNullOrWhiteSpace(backup.Adapter) || backup.Servers.Count == 0)
            return false;
        var primary = backup.Servers[0];
        var secondary = backup.Servers.Count > 1 ? backup.Servers[1] : null;
        RunElevatedNetsh(backup.Adapter, primary, secondary);
        PrivacyStore.Log("dns_restored", backup.Adapter);
        return true;
    }

    private static void RunElevatedNetsh(string adapter, string primary, string? secondary)
    {
        var escaped = adapter.Replace("'", "''");
        var script = $"Set-DnsClientServerAddress -InterfaceAlias '{escaped}' -ServerAddresses {primary}" +
                     (string.IsNullOrWhiteSpace(secondary) ? "" : $",{secondary}");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = true,
            Verb = "runas"
        };
        System.Diagnostics.Process.Start(psi)?.WaitForExit();
    }
}
