using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeGoogleKit.Licensing;
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

    public const string AgentSystemPrompt =
        "You are DeGoogle Kit’s coach — a chatbot whose job is helping this person leave Google on their terms.\n" +
        "Primary job: de-Google (Gmail, Photos, Drive, Chrome, Takeout, 2FA, passwords, DNS, phone/GrapheneOS, GDPR, leftovers on this Windows PC). " +
        "Use the local scan and guides as facts. Prefer Proton, Firefox/LibreWolf, Bitwarden/Proton Pass, Nextcloud, GrapheneOS, Ente, Quad9. " +
        "Never suggest Google or Gemini as the destination. There is no honest 1:1 YouTube replacement (FreeTube/NewPipe still use YouTube’s catalog). Brave is still Chromium. " +
        "This app never auto-deletes Google. Warn before destructive steps. Be an agent: ask one clarifying question when needed, then give the next concrete step.\n" +
        "You may briefly help with related privacy, Windows, and replacement-app questions.\n" +
        "You are not a general everyday AI. If they ask for homework, generic coding, or casual chat, answer in one short sentence and steer back: this coach is for leaving Google, not a ChatGPT replacement.\n" +
        "Keep replies under 250 words unless they ask for detail.";

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

    public static async Task<string> CloudAnswer(
        string question,
        ScanSnapshot scan,
        IReadOnlyList<GuideItem> guide,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        if (!PrivacyStore.Consent.CloudAiConsent)
            return "Tick the consent box on this tab first (GDPR). Then paste a Groq key once and send.";

        var settings = PrivacyStore.Settings;
        var def = AiProviders.Find(settings.AiProvider);
        var key = SecretStore.LoadApiKey() ?? "";
        var model = AiProviders.NormalizeModel(def.Id, settings.AiModel);
        if (!string.Equals(settings.AiModel, model, StringComparison.Ordinal))
        {
            settings.AiModel = model;
            PrivacyStore.SaveSettings(settings);
        }
        var chatUrl = def.NeedsEndpoint
            ? AiProviders.NormalizeChatUrl(settings.AiBaseUrl)
            : def.ChatUrl;

        if (AiProviders.IsGoogleBlocked(def.Id, def.Name, def.ChatUrl, chatUrl, model, settings.AiBaseUrl))
            return "Google / Gemini is not allowed in DeGoogle Kit.";

        if (!def.AllowMissingKey && string.IsNullOrWhiteSpace(key))
            return "Paste a Groq API key (Get a key → console.groq.com) and tick consent. The key stays on this Windows user (DPAPI). We never use Google AI.";

        if (def.NeedsEndpoint && string.IsNullOrWhiteSpace(settings.AiBaseUrl))
            return "Paste an OpenAI-compatible endpoint (Ollama, OpenClaw, LM Studio, LiteLLM…).";

        var messages = BuildAgentMessages(question, scan, guide, history);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DeGoogleKit/" + AppInfo.VersionText);

        PrivacyStore.Log("cloud_ai_request", def.Id);
        try
        {
            if (def.Anthropic)
                return await CallAnthropic(http, key,
                    OfflineGuide.ForHosted(question, scan, guide) + "\n\nUser: " + question, model, ct);
            var answer = await CallOpenAi(http, key, messages, chatUrl, model, def.Headers, def.Id, ct);
            return answer;
        }
        catch (Exception ex)
        {
            return "Cloud AI failed: " + ex.Message;
        }
    }

    private static List<object> BuildAgentMessages(
        string question,
        ScanSnapshot scan,
        IReadOnlyList<GuideItem> guide,
        IReadOnlyList<ChatMessage> history)
    {
        var messages = new List<object>
        {
            new { role = "system", content = AgentSystemPrompt },
            new
            {
                role = "user",
                content = "Facts for this session (local scan + matching guides — treat as true):\n" +
                          OfflineGuide.ForHosted(question, scan, guide)
            }
        };
        foreach (var turn in history.TakeLast(10))
        {
            if (string.IsNullOrWhiteSpace(turn.Text)) continue;
            if (turn.Text.StartsWith("Contacting ", StringComparison.Ordinal)) continue;
            var role = turn.Role.Equals("You", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant";
            messages.Add(new { role, content = Trim(turn.Text, 1600) });
        }
        var lastUser = history.LastOrDefault(m => m.Role.Equals("You", StringComparison.OrdinalIgnoreCase));
        if (lastUser is null || !string.Equals(lastUser.Text.Trim(), question.Trim(), StringComparison.Ordinal))
            messages.Add(new { role = "user", content = question.Trim() });
        return messages;
    }

    public const string HostedModelLabel = "Groq GPT-OSS 120B";

    public static string HostedAnswerUnavailable()
    {
        if (!LicenseService.IsCloudPass)
            return "DeGoogle AI is Cloud Pass (" + LicenseService.CloudMonthly + " or " + LicenseService.CloudYearly +
                   "). We pay " + HostedModelLabel + " (about $0.15 / $0.60 per million tokens), so this is the only subscription.\n\n" +
                   "We are not charging Cloud Pass until the live Stripe catalog is on. Use Ask AI with your own key, or Offline.";
        return "Cloud Pass is on, but hosted DeGoogle AI could not start. Use Ask AI with your key, or Offline.";
    }

    public static async Task<string> HostedAnswer(
        string question,
        ScanSnapshot scan,
        IReadOnlyList<GuideItem> guide,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        if (!PrivacyStore.Consent.CloudAiConsent)
            return "Cloud AI is off. Tick the consent box on the Coach tab first. Hosted DeGoogle AI sends your question to our license server, then Groq — never Google.";

        if (!LicenseService.IsCloudPass)
            return HostedAnswerUnavailable();

        var key = LicenseService.HostedLicenseKey;
        if (string.IsNullOrWhiteSpace(key) || !PassKeys.TryValidateCloud(key, out _))
            return "Cloud Pass is marked on this PC, but there is no Cloud Pass key to prove it to the license server. Activate a Cloud Pass key on the Pro tab.";

        var q = (question ?? "").Trim();
        if (q.Length == 0) q = "what should I do first?";

        var url = StripeStore.LicenseApiUrl + "/v1/coach";
        var body = new
        {
            question = q,
            context = OfflineGuide.ForHosted(q, scan, guide),
            history = history
                .Where(m => !string.IsNullOrWhiteSpace(m.Text) && !m.Text.StartsWith("Contacting ", StringComparison.Ordinal))
                .TakeLast(10)
                .Select(m => new
                {
                    role = m.Role.Equals("You", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant",
                    content = Trim(m.Text, 1600)
                })
                .ToList()
        };

        PrivacyStore.Log("hosted_ai_request", "groq");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DeGoogleKit/" + AppInfo.VersionText);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var res = await http.PostAsync(url, JsonContent(body), ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.TryGetProperty("answer", out var answer) && !string.IsNullOrWhiteSpace(answer.GetString()))
                return answer.GetString()!.Trim();
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : json;
            if (res.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                return "Hosted DeGoogle AI is wired, but the license server has no Groq key yet. Add GROQ_API_KEY to license-api and keep the server running. Meanwhile use Ask AI with your own key.";
            if (res.StatusCode == (System.Net.HttpStatusCode)402)
                return HostedAnswerUnavailable();
            return string.IsNullOrWhiteSpace(err) ? ("Hosted AI HTTP " + (int)res.StatusCode) : err;
        }
        catch (HttpRequestException)
        {
            return "Could not reach the license server at " + StripeStore.LicenseApiUrl +
                   ". Start license-api (it must have GROQ_API_KEY) or set DGK_LICENSE_API. Offline answers still work.";
        }
        catch (Exception ex)
        {
            return "Hosted AI failed: " + ex.Message;
        }
    }

    private static async Task<string> CallOpenAi(HttpClient http, string key, List<object> messages, string url, string model,
        (string Name, string Value)[] extraHeaders, string providerId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(key))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        foreach (var (name, value) in extraHeaders)
            http.DefaultRequestHeaders.TryAddWithoutValidation(name, value);

        async Task<(int Status, string Json)> PostAsync(string useModel)
        {
            object body;
            if (useModel.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
            {
                body = new
                {
                    model = useModel,
                    max_tokens = 1200,
                    reasoning_effort = "low",
                    messages
                };
            }
            else
            {
                body = new
                {
                    model = useModel,
                    temperature = 0.3,
                    max_tokens = 1200,
                    messages
                };
            }
            using var res = await http.PostAsync(url, JsonContent(body), ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            return ((int)res.StatusCode, json);
        }

        var (status, json) = await PostAsync(model);
        if (status == 404 && providerId == "groq")
        {
            foreach (var fallback in new[] { "openai/gpt-oss-120b", "openai/gpt-oss-20b" })
            {
                if (model.Equals(fallback, StringComparison.OrdinalIgnoreCase)) continue;
                (status, json) = await PostAsync(fallback);
                if (status is >= 200 and < 300)
                {
                    var settings = PrivacyStore.Settings;
                    settings.AiModel = fallback;
                    PrivacyStore.SaveSettings(settings);
                    return ReadOpenAiContent(json);
                }
            }
        }
        if (status is < 200 or >= 300)
            return FriendlyProviderError(status, json, model);
        return ReadOpenAiContent(json);
    }

    private static string ReadOpenAiContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        if (msg.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String) sb.Append(part.GetString());
                    else if (part.TryGetProperty("text", out var t)) sb.Append(t.GetString());
                }
                if (sb.Length > 0) return sb.ToString().Trim();
            }
        }
        if (msg.TryGetProperty("reasoning", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
        {
            var text = reasoning.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return "The model returned an empty answer. Try again, or use Offline.";
    }

    private static string FriendlyProviderError(int status, string json, string model)
    {
        if (status == 404)
            return "Groq returned 404 for model “" + model +
                   "”. That usually means an old Llama id (those 404 now). DeGoogle Kit uses openai/gpt-oss-120b. Leave Model blank under Advanced and send again. Key: console.groq.com/keys";
        if (status == 401 || status == 403)
            return "That API key was rejected. Get a Groq key at console.groq.com/keys (it starts with gsk_) and paste it in Connect Groq.";
        return $"Provider error {status}: {Trim(json, 400)}";
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
            system = AgentSystemPrompt
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
