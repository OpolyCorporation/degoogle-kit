using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class AiCoach
{
    public static string LocalPlan(ScanSnapshot scan, IReadOnlyList<GuideItem> guide)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Personal de-Google plan (built on this PC, no cloud).");
        sb.AppendLine();

        if (scan.DefaultBrowserIsGoogle)
            sb.AppendLine("1. Install Firefox or Brave and set it as the default browser in Windows Settings *before* uninstalling Chrome.");
        else
            sb.AppendLine("1. Default browser is already not Chrome. Good.");

        var apps = scan.Apps.Where(a => !a.IsUpdater).ToList();
        if (apps.Count > 0)
            sb.AppendLine($"2. Google desktop software still installed: {string.Join(", ", apps.Select(a => a.Name))}.");
        else
            sb.AppendLine("2. No Google desktop apps showed up in Add/Remove Programs.");
        if (scan.ChromeExtensions.Count > 0)
            sb.AppendLine($"   Chrome still has {scan.ChromeExtensions.Distinct().Count()} Google-related extension(s).");
        if (scan.GoogleServices.Count > 0)
            sb.AppendLine($"   Windows services still named Google/Chrome: {string.Join(", ", scan.GoogleServices.Distinct().Take(6))}.");
        if (scan.GoogleProcesses.Count > 0)
            sb.AppendLine($"   Running now: {string.Join(", ", scan.GoogleProcesses.Distinct())}.");
        if (scan.SignedInEmails.Count > 0)
            sb.AppendLine($"   Chrome still has {scan.SignedInEmails.Count} Google account(s) signed in.");
        if (scan.StartupEntries.Count > 0)
            sb.AppendLine($"   Starts with Windows: {string.Join(", ", scan.StartupEntries.Distinct().Take(6))}.");

        if (scan.DnsLooksLikeGoogle)
            sb.AppendLine("3. This PC uses Google DNS (8.8.8.8). Switch to Quad9 in the Network tab (Lifetime Pro, reversible).");
        else
            sb.AppendLine("3. DNS does not look like Google Public DNS.");

        var next = guide.Where(g => !g.IsDone).Take(5).ToList();
        if (next.Count > 0)
        {
            sb.AppendLine("4. Next guide steps:");
            foreach (var item in next)
                sb.AppendLine($"   • {item.Title} (instead of {item.GoogleProduct})");
        }
        else sb.AppendLine("4. Guide is complete. Do a Google Takeout, then consider deleting leftover services.");

        sb.AppendLine("5. GDPR: use the Rights tab for access, portability (Takeout), and erasure. Do Takeout before erasure.");
        sb.AppendLine("6. Do not wipe Chrome/Drive folders by hand — you can lose passwords and files.");
        return sb.ToString();
    }

    public static string LocalAnswer(string question, ScanSnapshot scan, IReadOnlyList<GuideItem> guide) =>
        OfflineGuide.Answer(question, scan, guide);

    public static string DefaultModel(string provider) => AiProviders.Find(provider).DefaultModel;

    public static bool HasUserAi()
    {
        if (!PrivacyStore.Consent.CloudAiConsent) return false;
        var def = AiProviders.Find(PrivacyStore.Settings.AiProvider);
        if (!string.IsNullOrWhiteSpace(SecretStore.LoadApiKey())) return true;
        if (def.Id == "compatible")
            return !string.IsNullOrWhiteSpace(PrivacyStore.Settings.AiBaseUrl) &&
                   AiProviders.IsLoopback(PrivacyStore.Settings.AiBaseUrl);
        return def.AllowMissingKey;
    }

    public static async Task<string> CloudAnswer(string question, ScanSnapshot scan, IReadOnlyList<GuideItem> guide, CancellationToken ct)
    {
        if (!PrivacyStore.Consent.CloudAiConsent)
            return "Cloud AI is off. Tick the consent box (GDPR Art. 7) on the Coach tab first.";

        var settings = PrivacyStore.Settings;
        var def = AiProviders.Find(settings.AiProvider);
        var key = SecretStore.LoadApiKey() ?? "";
        var model = string.IsNullOrWhiteSpace(settings.AiModel) ? def.DefaultModel : settings.AiModel;
        var chatUrl = def.NeedsEndpoint
            ? AiProviders.NormalizeChatUrl(settings.AiBaseUrl)
            : def.ChatUrl;

        if (AiProviders.IsGoogleBlocked(def.Id, def.Name, def.ChatUrl, chatUrl, model, settings.AiBaseUrl))
            return "Google / Gemini is not allowed in DeGoogle Kit.";

        if (!def.AllowMissingKey && string.IsNullOrWhiteSpace(key))
            return "Save an API key on the Coach tab. Keys stay on this Windows user account (DPAPI). We never use Google AI.";

        if (def.NeedsEndpoint && string.IsNullOrWhiteSpace(settings.AiBaseUrl))
            return "Paste an OpenAI-compatible endpoint (Ollama, OpenClaw, LM Studio, LiteLLM…).";

        var plan = LocalPlan(scan, guide);
        var catalog = OfflineGuide.Corpus();
        var payload =
            $"User question:\n{question}\n\nLocal scan context (may include app names on this PC):\n{plan}\n\nFact-checked offline guides (prefer these; do not invent a 1:1 YouTube replacement; never suggest Google as the destination):\n{catalog}";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DeGoogleKit/" + AppInfo.VersionText);

        PrivacyStore.Log("cloud_ai_request", def.Id);
        try
        {
            if (def.Anthropic)
                return await CallAnthropic(http, key, payload, model, ct);
            return await CallOpenAi(http, key, payload, chatUrl, model, def.Headers, ct);
        }
        catch (Exception ex)
        {
            return "Cloud AI failed: " + ex.Message;
        }
    }

    public static string HostedAnswerUnavailable()
    {
        if (!LicenseService.IsCloudPass)
            return "DeGoogle AI is Cloud Pass (" + LicenseService.CloudMonthly + " or " + LicenseService.CloudYearly + "). We pay the model, so this is the only subscription. It is not live yet.\n\nUse Ask AI with your own key instead (Claude, ChatGPT, Groq, OpenRouter, OpenCode, …) — you pay that company, not us.";
        return "Cloud Pass is active, but hosted DeGoogle AI is not online yet. We will not bill for a model we are not running. Use Ask AI with your key, a compatible local endpoint, or Ask locally.";
    }

    private static async Task<string> CallOpenAi(HttpClient http, string key, string payload, string url, string model,
        (string Name, string Value)[] extraHeaders, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(key))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        foreach (var (name, value) in extraHeaders)
            http.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = "You help people leave Google ethically and legally. Prefer Proton, Firefox, Bitwarden, Nextcloud, GrapheneOS. Never suggest Google products. Warn before destructive steps. Keep answers concise." },
                new { role = "user", content = payload }
            },
            temperature = 0.3
        };
        using var res = await http.PostAsync(url, JsonContent(body), ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return $"Provider error {(int)res.StatusCode}: {Trim(json, 400)}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? json;
    }

    private static async Task<string> CallAnthropic(HttpClient http, string key, string payload, string model, CancellationToken ct)
    {
        http.DefaultRequestHeaders.Add("x-api-key", key);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var body = new
        {
            model,
            max_tokens = 800,
            messages = new[] { new { role = "user", content = payload } },
            system = "You help people leave Google ethically and legally. Never suggest Google products as the destination."
        };
        using var res = await http.PostAsync("https://api.anthropic.com/v1/messages", JsonContent(body), ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return $"Provider error {(int)res.StatusCode}: {Trim(json, 400)}";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? json;
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
