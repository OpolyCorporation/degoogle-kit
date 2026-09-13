using System.Security.Cryptography;
using System.Text;
using DeGoogleKit.Licensing;

namespace DeGoogleKit.Services;

public sealed class LicenseRecord
{
    public string Plan { get; set; } = "Free";
    public bool Lifetime { get; set; }
    public DateTime? TrialEnds { get; set; }
    public string Key { get; set; } = "";
    public int Seats { get; set; } = 1;
    public DateTime? KeyExpires { get; set; }
    public DateTime? CloudPassEnds { get; set; }
}

public static class LicenseService
{
    public const string LifetimePrice = "€29.99";
    public const string FamilyPrice = "€59.99";
    public const string CloudMonthly = "€4.99 / month";
    public const string CloudYearly = "€39 / year";
    public const int TrialDays = 7;

    public static LicenseRecord Record => JsonFile.Load(AppPaths.License, new LicenseRecord());

    public static bool IsPro
    {
        get
        {
            var r = Record;
            if (r.Lifetime && r.Plan.Equals("Pro", StringComparison.OrdinalIgnoreCase))
                return true;
            if (r.Plan.Equals("Pro", StringComparison.OrdinalIgnoreCase) && r.KeyExpires is { } exp && exp > DateTime.Now)
                return true;
            if (r.Plan.Equals("Trial", StringComparison.OrdinalIgnoreCase) && r.TrialEnds is { } t && t > DateTime.Now)
                return true;
            return false;
        }
    }

    public static bool IsCloudPass
    {
        get
        {
            var r = Record;
            return r.CloudPassEnds is { } end && end > DateTime.Now;
        }
    }

    public static string StatusText
    {
        get
        {
            var parts = new List<string>();
            var r = Record;
            if (r.Lifetime && r.Plan.Equals("Pro", StringComparison.OrdinalIgnoreCase))
                parts.Add(r.Seats > 1 ? $"Pro family ({r.Seats} PCs)" : "Pro lifetime");
            else if (r.Plan.Equals("Pro", StringComparison.OrdinalIgnoreCase) && r.KeyExpires is { } exp && exp > DateTime.Now)
                parts.Add($"Pro — until {exp:d}");
            else if (r.Plan.Equals("Trial", StringComparison.OrdinalIgnoreCase) && r.TrialEnds is { } t && t > DateTime.Now)
                parts.Add($"Pro trial — {Math.Max(0, (t.Date - DateTime.Today).Days)} days left");

            if (IsCloudPass)
                parts.Add($"Cloud Pass — until {r.CloudPassEnds:d}");

            return parts.Count == 0 ? "Free" : string.Join(" · ", parts);
        }
    }

    public static bool StartTrial()
    {
        var r = Record;
        if (r.Lifetime) return false;
        if (r.TrialEnds is not null) return false;
        r.Plan = "Trial";
        r.TrialEnds = DateTime.Now.AddDays(TrialDays);
        JsonFile.Save(AppPaths.License, r);
        PrivacyStore.Log("trial_started", r.TrialEnds.Value.ToString("u"));
        return true;
    }

    public static void ApplyCloudTrial(DateTimeOffset startedAt)
    {
        var r = Record;
        if (r.Lifetime && r.Plan.Equals("Pro", StringComparison.OrdinalIgnoreCase)) return;
        var ends = startedAt.LocalDateTime.AddDays(TrialDays);
        if (r.TrialEnds is { } local && local > DateTime.Now) return;
        r.TrialEnds = ends;
        if (ends > DateTime.Now)
            r.Plan = "Trial";
        else if (r.Plan.Equals("Trial", StringComparison.OrdinalIgnoreCase))
            r.Plan = "Free";
        JsonFile.Save(AppPaths.License, r);
    }

    public static bool Activate(string key)
    {
        key = (key ?? "").Trim();
        if (LicenseTicket.TryVerify(key, out var payload))
        {
            var paid = Record;
            paid.Key = key;
            if (LicenseTicket.IsPaidPro(payload))
            {
                paid.Plan = "Pro";
                paid.Lifetime = true;
                paid.Seats = Math.Max(1, payload.Seats);
                paid.KeyExpires = null;
                PrivacyStore.Log("license_activated", payload.Sku + " " + payload.Sid);
            }
            else if (LicenseTicket.IsCloud(payload))
            {
                paid.CloudPassEnds = DateTime.Today.AddYears(1);
                PrivacyStore.Log("cloud_pass_activated", payload.Sid);
            }
            else return false;
            JsonFile.Save(AppPaths.License, paid);
            return true;
        }

#if DEBUG
        if (!TryParse(key, out var kind, out var expires)) return false;
        var r = Record;
        r.Key = key.ToUpperInvariant();
        if (kind == "lifetime")
        {
            r.Plan = "Pro";
            r.Lifetime = true;
            r.KeyExpires = null;
            PrivacyStore.Log("license_activated", "legacy-lifetime");
        }
        else if (kind == "cloud")
        {
            r.CloudPassEnds = expires;
            PrivacyStore.Log("cloud_pass_activated", expires!.Value.ToString("d"));
        }
        else
        {
            r.Plan = "Pro";
            r.Lifetime = false;
            r.KeyExpires = expires;
            PrivacyStore.Log("license_activated", expires!.Value.ToString("d"));
        }
        JsonFile.Save(AppPaths.License, r);
        return true;
#else
        return false;
#endif
    }

    public static string IssueLifetimeKey() =>
        $"DGKL-LIFE-{Checksum("DGKL-LIFE")}";

    public static string IssueCloudKey(DateTime expires) =>
        $"DGKC-{expires:yyyyMMdd}-{Checksum($"DGKC-{expires:yyyyMMdd}")}";

    public static string IssueLocalKey(DateTime expires) => IssueCloudKey(expires);

    public static bool TryParse(string key, out DateTime expires)
    {
        var ok = TryParse(key, out var kind, out var date);
        expires = date ?? default;
        return ok && kind != "lifetime";
    }

    public static bool TryParse(string key, out string kind, out DateTime? expires)
    {
        kind = "";
        expires = null;
        var parts = (key ?? "").Trim().ToUpperInvariant().Split('-');
        if (parts.Length != 3) return false;

        if (parts[0] == "DGKL" && parts[1] == "LIFE")
        {
            if (!string.Equals(parts[2], Checksum("DGKL-LIFE"), StringComparison.OrdinalIgnoreCase))
                return false;
            kind = "lifetime";
            return true;
        }

        if (parts[0] is "DGKC" or "DGK1")
        {
            if (!DateTime.TryParseExact(parts[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
                return false;
            var payload = $"{parts[0]}-{parts[1]}";
            if (!string.Equals(parts[2], Checksum(payload), StringComparison.OrdinalIgnoreCase))
                return false;
            if (date.Date < DateTime.Today) return false;
            kind = parts[0] == "DGKC" ? "cloud" : "legacy";
            expires = date.Date;
            return true;
        }

        return false;
    }

    private static string Checksum(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload + "|degoogle-kit-license-v1"));
        return Convert.ToHexString(bytes)[..6];
    }
}
