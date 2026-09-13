using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeGoogleKit.Licensing;

namespace DeGoogleKit.LicenseApi;

public static class CoachEndpoints
{
    public const string DefaultModel = "openai/gpt-oss-120b";
    public const string DefaultUrl = "https://api.groq.com/openai/v1/chat/completions";

    private const int MaxQuestionChars = 2000;
    private const int MaxContextChars = 24_000;
    private const int PerMinute = 6;
    private const int PerHour = 40;
    private const int MaxTokens = 900;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly ConcurrentDictionary<string, List<long>> Hits = new();

    private static readonly string SystemPrompt =
        "You are DeGoogle Kit’s coach — a chatbot whose job is helping this person leave Google on their terms. " +
        "Primary job: de-Google (mail, photos, Chrome, Takeout, 2FA, DNS, phone, GDPR). " +
        "Use the supplied scan and guides as facts. Prefer Proton, Firefox/LibreWolf, Bitwarden/Proton Pass, Nextcloud, GrapheneOS, Ente, Quad9. " +
        "Never suggest Google or Gemini as the destination. No honest 1:1 YouTube replacement. Brave is still Chromium. " +
        "This app never auto-deletes Google. Be an agent: ask one clarifying question when needed, then the next concrete step. " +
        "You may briefly help with related privacy questions. You are not a general everyday AI — if they go off-topic, answer shortly and steer back. Keep replies under 250 words unless they ask for detail.";

    public static void Map(WebApplication app)
    {
        app.MapPost("/v1/coach", Handle);
    }

    public static string ConfiguredModel(IConfiguration config) => Model(config);

    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(ApiKey(config)) &&
        !IsGoogleBlocked(Model(config), ChatUrl(config));

    private static async Task<IResult> Handle(HttpRequest request, IConfiguration config)
    {
        if (!IsConfigured(config))
            return Results.Json(new { error = "Hosted DeGoogle AI is not configured (missing GROQ_API_KEY / COACH_API_KEY)." }, statusCode: 503);

        var model = Model(config);
        var url = ChatUrl(config);
        if (IsGoogleBlocked(model, url))
            return Results.Json(new { error = "Google / Gemini is not allowed." }, statusCode: 400);

        var license = ReadLicense(request);
        if (!PassKeys.TryValidateCloud(license, out var expires))
            return Results.Json(new { error = "Cloud Pass is required for DeGoogle AI. Activate a Cloud Pass key first." }, statusCode: 402);

        var bucket = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(license)))[..16];
        if (!Allow(bucket))
            return Results.Json(new { error = "Slow down — Cloud Pass is rate-limited to keep Groq cheap (about 6/min, 40/hour)." }, statusCode: 429);

        string json;
        using (var reader = new StreamReader(request.Body))
            json = await reader.ReadToEndAsync();
        using var body = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var question = Read(body.RootElement, "question");
        var context = Read(body.RootElement, "context");
        if (question.Length == 0)
            return Results.Json(new { error = "Ask a question about leaving Google." }, statusCode: 400);
        if (question.Length > MaxQuestionChars)
            question = question[..MaxQuestionChars];
        if (context.Length > MaxContextChars)
            context = context[..MaxContextChars];

        var user = "User question:\n" + question + "\n\nLocal scan and fact-checked guides:\n" + context;
        try
        {
            var answer = await CallGroq(ApiKey(config), url, model, user);
            return Results.Ok(new
            {
                answer,
                model,
                provider = "groq",
                cloud_pass_until = expires.ToString("u")
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new { error = "Hosted AI failed: " + ex.Message }, statusCode: 502);
        }
    }

    private static async Task<string> CallGroq(string key, string url, string model, string user)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.TryAddWithoutValidation("User-Agent", "DeGoogleKit-LicenseApi");
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            ["messages"] = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = user }
            }
        };
        if (model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
            payload["reasoning_effort"] = "low";
        else
            payload["temperature"] = 0.3;
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();
        if ((int)res.StatusCode == 404 && !model.Equals("openai/gpt-oss-20b", StringComparison.OrdinalIgnoreCase))
            return await CallGroq(key, url, "openai/gpt-oss-20b", user);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Provider error {(int)res.StatusCode}: {Trim(json, 400)}");
        using var doc = JsonDocument.Parse(json);
        return ReadMessage(doc.RootElement);
    }

    private static string ReadMessage(JsonElement root)
    {
        var msg = root.GetProperty("choices")[0].GetProperty("message");
        if (msg.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                        sb.Append(part.GetString());
                    else if (part.TryGetProperty("text", out var t))
                        sb.Append(t.GetString());
                }
                if (sb.Length > 0) return sb.ToString();
            }
        }
        if (msg.TryGetProperty("reasoning", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
            return reasoning.GetString() ?? "";
        return "The model returned an empty answer. Try again, or use Offline on the Coach tab.";
    }

    private static string ReadLicense(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();
        var header = request.Headers["X-DeGoogle-License"].ToString();
        return string.IsNullOrWhiteSpace(header) ? "" : header.Trim();
    }

    private static bool Allow(string bucket)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var list = Hits.GetOrAdd(bucket, _ => []);
        lock (list)
        {
            list.RemoveAll(t => now - t > 3600);
            if (list.Count(t => now - t < 60) >= PerMinute) return false;
            if (list.Count >= PerHour) return false;
            list.Add(now);
            return true;
        }
    }

    private static string ApiKey(IConfiguration config) =>
        First(Environment.GetEnvironmentVariable("GROQ_API_KEY"),
            Environment.GetEnvironmentVariable("COACH_API_KEY"),
            config["Coach:ApiKey"]);

    private static string Model(IConfiguration config) =>
        First(Environment.GetEnvironmentVariable("COACH_MODEL"), config["Coach:Model"], DefaultModel);

    private static string ChatUrl(IConfiguration config) =>
        First(Environment.GetEnvironmentVariable("COACH_URL"), config["Coach:Url"], DefaultUrl);

    private static string First(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return "";
    }

    private static string Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? (el.GetString() ?? "").Trim() : "";

    private static bool IsGoogleBlocked(params string?[] values) =>
        values.Any(v =>
            !string.IsNullOrWhiteSpace(v) &&
            (v.Contains("google", StringComparison.OrdinalIgnoreCase) ||
             v.Contains("gemini", StringComparison.OrdinalIgnoreCase) ||
             v.Contains("generativelanguage", StringComparison.OrdinalIgnoreCase)));

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
