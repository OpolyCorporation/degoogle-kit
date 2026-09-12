using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeGoogleKit.Licensing;

public sealed class LicensePayload
{
    public int V { get; set; } = 1;
    public string Sku { get; set; } = "";
    public int Seats { get; set; } = 1;
    public string Sid { get; set; } = "";
    public long Iat { get; set; }
}

public static class LicenseTicket
{
    public const string Prefix = "DGK2.";

    public const string PublicKeyPem =
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEzv091QhNNbaVdX9iys2JO+168w2N
        7cX1SmF3aauEAaONJf+huC3L2ygj6YPGYwR6x6J/jNtxcLkYY/j5Fia9pQ==
        -----END PUBLIC KEY-----
        """;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Sign(LicensePayload payload, string privateKeyPem)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        var bytes = Encoding.UTF8.GetBytes(json);
        using var ecdsa = ECDsa.Create();
        if (privateKeyPem.Contains("BEGIN", StringComparison.Ordinal))
            ecdsa.ImportFromPem(privateKeyPem);
        else
            ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPem.Trim()), out _);
        var sig = ecdsa.SignData(bytes, HashAlgorithmName.SHA256);
        return Prefix + B64(bytes) + "." + B64(sig);
    }

    public static bool TryVerify(string key, out LicensePayload payload)
    {
        payload = new LicensePayload();
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var parts = key.Trim().Split('.');
        if (parts.Length != 3) return false;
        byte[] jsonBytes;
        byte[] sig;
        try
        {
            jsonBytes = FromB64(parts[1]);
            sig = FromB64(parts[2]);
        }
        catch
        {
            return false;
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(PublicKeyPem);
        if (!ecdsa.VerifyData(jsonBytes, sig, HashAlgorithmName.SHA256))
            return false;

        var parsed = JsonSerializer.Deserialize<LicensePayload>(jsonBytes, Json);
        if (parsed is null || parsed.V != 1 || string.IsNullOrWhiteSpace(parsed.Sku))
            return false;
        payload = parsed;
        return true;
    }

    public static bool IsPaidPro(LicensePayload p) =>
        p.Sku is "lifetime" or "family";

    public static bool IsCloud(LicensePayload p) =>
        p.Sku.StartsWith("cloud", StringComparison.OrdinalIgnoreCase);

    private static string B64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
