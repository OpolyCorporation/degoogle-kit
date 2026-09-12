using System.Security.Cryptography;
using System.Text;

namespace DeGoogleKit.Services;

public static class SecretStore
{
    public static void SaveApiKey(string apiKey)
    {
        var settings = PrivacyStore.Settings;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            settings.ProtectedApiKey = "";
        }
        else
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(apiKey.Trim()),
                Encoding.UTF8.GetBytes("DeGoogleKit"),
                DataProtectionScope.CurrentUser);
            settings.ProtectedApiKey = Convert.ToBase64String(protectedBytes);
        }
        PrivacyStore.SaveSettings(settings);
        PrivacyStore.Log("api_key_updated", string.IsNullOrWhiteSpace(apiKey) ? "cleared" : "saved locally with Windows DPAPI");
    }

    public static string? LoadApiKey()
    {
        try
        {
            var blob = PrivacyStore.Settings.ProtectedApiKey;
            if (string.IsNullOrWhiteSpace(blob)) return null;
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(blob),
                Encoding.UTF8.GetBytes("DeGoogleKit"),
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
