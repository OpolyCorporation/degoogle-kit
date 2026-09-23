namespace DeGoogleKit.Services;

/// <summary>
/// Messages shown to end users. Never mention .env, GROQ_API_KEY, license-api paths,
/// localhost defaults, or other operator setup.
/// </summary>
public static class UserFacing
{
    public static string HostedAiUnavailable()
    {
        if (!LicenseService.IsCloudPass)
            return "DeGoogle AI (Cloud Pass) is not for sale yet. When it is live, we run the model for you.\n\n" +
                   "Today: use Offline or Topics (no key), or connect your own key under Connect and press Send.";
        return "Cloud Pass is on this PC, but DeGoogle AI is not available right now. Use Offline, Topics, or Send with your own key.";
    }

    public static string HostedAiOffline() =>
        "DeGoogle AI is not available right now. Use Offline or Topics, or Send with your own key.";

    public static string HostedAiNeedsConsent() =>
        "Tick the consent box on this tab first. DeGoogle AI only runs when you agree to send your question (never to Google).";

    public static string LicenseRedeemOffline() =>
        "Could not finish activating that payment from this PC. Keep this window open after Checkout, or paste the license key from your email / receipt. Offline features still work.";

    public static string LicenseServerBusy() =>
        "Could not confirm that payment right now. Try again in a minute, or paste the license key if you already received one.";

    public static string SanitizeProviderError(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Something went wrong. Try Offline, or Send again.";
        var s = raw;
        if (s.Contains("GROQ_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("COACH_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("license-api", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("DGK_LICENSE", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("LICENSE_SIGNING", StringComparison.OrdinalIgnoreCase))
            return HostedAiOffline();
        if (s.Length > 400) s = s[..400] + "…";
        return s;
    }
}
