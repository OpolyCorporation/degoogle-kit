using System.Security.Cryptography;
using System.Text;

namespace DeGoogleKit.Licensing;

public static class PassKeys
{
    public static string Checksum(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload + "|degoogle-kit-license-v1"));
        return Convert.ToHexString(bytes)[..6];
    }

    public static DateTime CloudExpiryUtc(LicensePayload payload)
    {
        var start = payload.Iat > 0
            ? DateTimeOffset.FromUnixTimeSeconds(payload.Iat).UtcDateTime
            : DateTime.UtcNow;
        var days = payload.Sku.Contains("month", StringComparison.OrdinalIgnoreCase) ? 31 : 366;
        return start.AddDays(days);
    }

    public static bool TryValidateCloud(string? key, out DateTime expiresUtc)
    {
        expiresUtc = default;
        key = (key ?? "").Trim();
        if (key.Length == 0) return false;

        if (LicenseTicket.TryVerify(key, out var payload) && LicenseTicket.IsCloud(payload))
        {
            expiresUtc = CloudExpiryUtc(payload);
            return expiresUtc > DateTime.UtcNow;
        }

        return TryParseLegacyCloud(key, out expiresUtc) && expiresUtc > DateTime.UtcNow;
    }

    public static bool TryParseLegacyCloud(string key, out DateTime expiresUtc)
    {
        expiresUtc = default;
        var parts = (key ?? "").Trim().ToUpperInvariant().Split('-');
        if (parts.Length != 3 || parts[0] != "DGKC") return false;
        if (!DateTime.TryParseExact(parts[1], "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var date))
            return false;
        var payload = $"{parts[0]}-{parts[1]}";
        if (!string.Equals(parts[2], Checksum(payload), StringComparison.OrdinalIgnoreCase))
            return false;
        expiresUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc).AddDays(1).AddTicks(-1);
        return date.Date >= DateTime.UtcNow.Date;
    }
}
