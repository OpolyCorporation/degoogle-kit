namespace DeGoogleKit.Services;

public static class StripeStore
{
    /// <summary>
    /// False while Checkout URLs are Stripe test Payment Links. Flip after the live
    /// catalog is created on the live Stripe account (not the sandbox).
    /// </summary>
    public const bool CatalogIsLive = false;

    public const string LifetimePaymentLink = "https://buy.stripe.com/test_fZu8wP7Vacgd6eRfVh2wU00";
    public const string FamilyPaymentLink = "https://buy.stripe.com/test_14AfZh7VagwtfPr4cz2wU01";

    public static string AfterCheckoutHint =>
        CatalogIsLive
            ? "Stripe Checkout opened in your browser.\n\nAfter payment, paste the session id (cs_…) into the box and click Activate after payment."
            : "Stripe Checkout opened in your browser (test mode — no live charges yet).\n\nAfter payment, paste the session id (cs_…) into the box and click Activate after payment. The license server must be running.";

    public static string LicenseApiUrl
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("DGK_LICENSE_API")?.Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/');
            var custom = PrivacyStore.Settings.LicenseApiUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(custom)) return custom.TrimEnd('/');
            return "http://127.0.0.1:5288";
        }
    }
}
