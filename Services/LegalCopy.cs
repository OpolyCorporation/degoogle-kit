namespace DeGoogleKit.Services;

/// <summary>
/// GDPR Arts. 12–14, Danish e-handelsloven identity, and consumer-contract facts.
/// Keep the in-app notice and the public website in the same words.
/// </summary>
public static class LegalCopy
{
    public const int NoticeVersion = 6;

    public const string ControllerName = "Opolyonix Corp";
    public const string Cvr = "43410369";
    public const string Address = "c/o Jack Jim Holst Jensen, Sportsvej 2, 2. th, 6705 Esbjerg Ø, Denmark";
    public const string PrivacyUrl = "https://opolycorporation.github.io/degoogle-kit/privacy.html";
    public const string TermsUrl = "https://opolycorporation.github.io/degoogle-kit/terms.html";
    public const string IssuesUrl = "https://github.com/OpolyCorporation/degoogle-kit/issues";
    public const string DatatilsynetUrl = "https://www.datatilsynet.dk/";

    /// <summary>
    /// Marketing / account site (Lovable). Update when a custom domain is published.
    /// </summary>
    public const string WebsiteUrl = "https://id-preview--d11fe691-44fe-4cc4-84a5-135f3b0e7341.lovable.app";
    public const string WebsiteAccountUrl = WebsiteUrl + "/account";

    public static string IdentityLine =>
        ControllerName + ", CVR " + Cvr + ", " + Address;

    public static string PrivacyNotice =>
        "Privacy notice (v" + NoticeVersion + ")\n\n" +
        "Controller: " + IdentityLine + "\n" +
        "Electronic contact for privacy and consumer requests: open a GitHub issue at " + IssuesUrl +
        " with the subject “Privacy (GDPR)” or “Withdrawal (consumer)”. Do not send Takeout archives or passwords.\n\n" +
        "Who processes what\n" +
        "• Local use (default): DeGoogle Kit runs on your Windows PC. Plan, checklist, scan results, Takeout files you choose, API keys, and trial/Pro status stay in AppData\\DeGoogleKit. You are in control of that copy. Opolyonix Corp does not receive it unless you later choose an optional cloud feature.\n" +
        "• Household invites: a Household owner may invite other emails from the website account page. Invitees receive hashed one-time website login codes until they claim a PC code; until then we do not share the Household license key or the owner's plan backup with that invitee.\n" +
        "• Optional free account: if you create an email/password account, Opolyonix Corp is the controller of that account. Processor: Supabase (Auth + Postgres), not Google. We store your email, a hashed password (Supabase Auth), your plan, your guide checklist, and whether this account started the Pro trial (a timestamp only) so the trial can follow you to another PC. We do not upload API keys, Takeout files, scan results, or clipboard contents.\n" +
        "• Payments: Stripe processes Checkout for Lifetime Pro / Household (Family). We do not store card numbers. After payment a signed license key can be linked to your account.\n" +
        "• Updates and this website: GitHub (Microsoft) hosts the Windows download, version feed, and these pages. Checking for updates is optional and sends the app version in the User-Agent.\n" +
        "• Optional Cloud AI: only if you tick a separate consent box. Ask AI with your own key sends the question and a local scan summary to the provider you pick (never Google Gemini). DeGoogle AI (Cloud Pass) sends the same kind of payload to our license server, which then calls Groq (GPT-OSS 120B) — we pay Groq; the key never sits in this app. We do not log the question. You can withdraw that consent on the Coach tab.\n\n" +
        "Purposes and legal bases (GDPR Art. 6)\n" +
        "• Provide the local app you asked to run — Art. 6(1)(b) contract / steps at your request.\n" +
        "• Optional account (email, plan, checklist backup) — Art. 6(1)(a) consent, given when you create or sign in to the account. You can withdraw by deleting the account; local files remain until you erase them.\n" +
        "• Paid license delivery and fraud prevention — Art. 6(1)(b) and 6(1)(f).\n" +
        "• Optional Cloud AI — Art. 6(1)(a) consent, separate from the account.\n" +
        "• Legal obligations (tax/accounting for a paid license) — Art. 6(1)(c).\n\n" +
        "We do not sell personal data, run ads, or use Google APIs. No automated decisions with legal or similarly significant effects (Art. 22).\n\n" +
        "Children: this app is not directed at children under 16. Use needs 16+ or a parent/guardian (GDPR Art. 8 default age).\n\n" +
        "Retention: local data until you erase it or uninstall. Account data until you delete the account (rows cascade). Stripe keeps payment records under its own retention. GitHub may log download IPs under its terms.\n\n" +
        "Transfers: local mode does not send your plan off this PC. An account uses Supabase over HTTPS. Prefer an EU Supabase region. If a processor stores data outside the EEA, Standard Contractual Clauses / an adequacy decision apply under that processor’s DPA. Stripe, GitHub, and Groq (hosted DeGoogle AI only) may process in the US with appropriate safeguards.\n\n" +
        "Your rights (Arts. 15–21): access, rectification, erasure, restriction, portability, and objection. In the app: Privacy → Export my data, Delete all local data, and Delete my cloud account. You can withdraw account consent by deleting the account, and Cloud AI consent on the Coach tab. Complaints: Datatilsynet (Denmark) " + DatatilsynetUrl + " or your local EEA authority. You may also contact us first via GitHub issues.\n\n" +
        "Permissions: the app asks before it scans this PC, opens a site, uses the clipboard, checks for updates, reads Takeout, writes Desktop files, stores keys, creates an account, uninstalls, changes DNS, or sends Cloud AI. Remembered allows can be revoked in Privacy.\n\n" +
        "Full public copy: " + PrivacyUrl + "\n" +
        "License terms: " + TermsUrl;

    public static string Terms =>
        "DeGoogle Kit terms (v" + NoticeVersion + ")\n\n" +
        "Trader: " + IdentityLine + "\n" +
        "Contact: " + IssuesUrl + "\n\n" +
        "The Windows app helps you leave Google on your terms. It never auto-deletes Google data or accounts. You choose each uninstall, DNS change, and Takeout step.\n\n" +
        "Free features (scan, plan, Takeout parse, catalog, GDPR templates versus Google, local coach, export/erasure of this app’s data) work without paying and without an account.\n\n" +
        "A free account is optional. It backs up plan and checklist only. You may delete it at any time.\n\n" +
        "Lifetime Pro (" + LicenseService.LifetimePrice + " once) and Household / Family (" + LicenseService.FamilyPrice + ", up to " + LicenseService.FamilySeats + " PCs) are one-time digital licenses sold through Stripe Checkout. Household is the same Pro extras for several Windows PCs in one home (one key per purchase). Cards never enter this app. Cloud Pass (" + LicenseService.CloudMonthly + " or " + LicenseService.CloudYearly + ") is hosted DeGoogle AI on Groq GPT-OSS 120B. It is not billed until the live Stripe catalog is on.\n\n" +
        "EU/EEA consumer withdrawal (forbrugeraftaleloven / Directive 2011/83/EU): you normally have 14 days to withdraw from a distance contract. Lifetime Pro is digital content delivered immediately as a license key after payment. If you ask us to supply it straight away, the 14-day right may be lost once the key is delivered and activated. If you have not activated the key, contact us within 14 days with subject “Withdrawal (consumer)” for a refund via Stripe. The Pro trial is " + LicenseService.TrialDays + " days, no card, and requires a signed-in free account so the trial can follow that account.\n\n" +
        "The software is provided as-is. It does not guarantee that Google or any replacement service will accept your export. You remain responsible for backups before you disconnect Google.\n\n" +
        "Governing law: Denmark, without limiting mandatory consumer protections in your EEA country of residence.";
}
