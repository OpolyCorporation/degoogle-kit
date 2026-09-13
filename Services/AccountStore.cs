using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeGoogleKit.Services;

public static class AccountStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DeGoogleKit.account");

    public static AccountSession? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.Account)) return null;
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(AppPaths.Account).Trim());
            var json = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser));
            return JsonSerializer.Deserialize<AccountSession>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AccountSession session)
    {
        AppPaths.EnsureRoot();
        var json = JsonSerializer.Serialize(session);
        var blob = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllText(AppPaths.Account, Convert.ToBase64String(blob));
        PrivacyStore.Log("account_session_saved", session.Email);
    }

    public static void Clear()
    {
        if (File.Exists(AppPaths.Account))
            File.Delete(AppPaths.Account);
    }
}
