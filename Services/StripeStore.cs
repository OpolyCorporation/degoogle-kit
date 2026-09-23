namespace DeGoogleKit.Services;

public static class StripeStore
{
    /// <summary>
    /// False while Checkout is still being verified end-to-end. Flip after a real
    /// paid→DGK2 key path works on the website (STRIPE_API_KEY + LICENSE_SIGNING_KEY).
    /// </summary>
    public static bool CatalogIsLive => false;

    /// <summary>
    /// Hosted DeGoogle AI (we pay Groq). Keep false until public coach + GROQ are live.
    /// </summary>
    public static bool HostedAiIsLive => false;

    public static bool LicenseApiLooksPublic
    {
        get
        {
            var url = LicenseApiUrl;
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            return !AiProviders.IsLoopback(url);
        }
    }

    /// <summary>Fallback Payment Links when the website has no Stripe secret yet.</summary>
    public const string LifetimePaymentLink = "https://buy.stripe.com/test_fZu8wP7Vacgd6eRfVh2wU00";
    public const string FamilyPaymentLink = "https://buy.stripe.com/test_14AfZh7VagwtfPr4cz2wU01";

    public static string LifetimeCheckoutUrl => LegalCopy.WebsiteCheckoutUrl + "?plan=lifetime";
    public static string FamilyCheckoutUrl => LegalCopy.WebsiteCheckoutUrl + "?plan=household";

    public static string AfterCheckoutHint =>
        "Checkout opened in your browser.\n\n" +
        "Tick the 14-day withdrawal box, pay with Stripe, then return here or allow the app to open with your key.\n" +
        "Keep this window open.";

    public const string PaidReturnUrl =
        "https://opolycorporation.github.io/degoogle-kit/paid.html?session_id={CHECKOUT_SESSION_ID}";

    /// <summary>
    /// License mint + account bind live on the marketing website (same origin as checkout).
    /// Override with DGK_LICENSE_API or Privacy settings if needed.
    /// </summary>
    public static string LicenseApiUrl
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("DGK_LICENSE_API")?.Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/');
            var custom = PrivacyStore.Settings.LicenseApiUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(custom)) return custom.TrimEnd('/');
            return LegalCopy.WebsiteUrl.TrimEnd('/');
        }
    }
}
