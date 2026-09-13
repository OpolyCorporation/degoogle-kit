namespace DeGoogleKit.Services;

public sealed class AiProviderDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Hint { get; init; }
    public required string KeyUrl { get; init; }
    public required string DefaultModel { get; init; }
    public string ChatUrl { get; init; } = "";
    public bool Anthropic { get; init; }
    public bool NeedsEndpoint { get; init; }
    public bool AllowMissingKey { get; init; }
    public (string Name, string Value)[] Headers { get; init; } = [];
}

public static class AiProviders
{
    public static readonly IReadOnlyList<AiProviderDef> All =
    [
        new()
        {
            Id = "groq",
            Name = "Groq (fast, cheap)",
            DefaultModel = "openai/gpt-oss-120b",
            ChatUrl = "https://api.groq.com/openai/v1/chat/completions",
            KeyUrl = "https://console.groq.com/keys",
            Hint = "Click Get a key → console.groq.com → API keys → paste it. We pick GPT-OSS 120B for you. Old Llama ids on Groq now 404."
        },
        new()
        {
            Id = "opencode",
            Name = "OpenCode (free models available)",
            DefaultModel = "big-pickle",
            ChatUrl = "https://opencode.ai/zen/v1/chat/completions",
            KeyUrl = "https://opencode.ai/console",
            AllowMissingKey = true,
            Hint = "OpenCode Zen. Free models such as big-pickle work with no key; paid models need a console key."
        },
        new()
        {
            Id = "openrouter",
            Name = "OpenRouter (one key, many models)",
            DefaultModel = "openai/gpt-4o-mini",
            ChatUrl = "https://openrouter.ai/api/v1/chat/completions",
            KeyUrl = "https://openrouter.ai/keys",
            Headers = [("HTTP-Referer", "https://degooglekit.local"), ("X-Title", "DeGoogle Kit")],
            Hint = "One key for Claude, ChatGPT, Llama, Mistral, DeepSeek, and more. Do not pick Gemini."
        },
        new()
        {
            Id = "openai",
            Name = "ChatGPT (OpenAI)",
            DefaultModel = "gpt-4o-mini",
            ChatUrl = "https://api.openai.com/v1/chat/completions",
            KeyUrl = "https://platform.openai.com/api-keys",
            Hint = "Get a key at platform.openai.com → API keys. You pay OpenAI, not us."
        },
        new()
        {
            Id = "anthropic",
            Name = "Claude (Anthropic)",
            DefaultModel = "claude-sonnet-4-5",
            Anthropic = true,
            KeyUrl = "https://console.anthropic.com/settings/keys",
            Hint = "Get a key at console.anthropic.com → API keys."
        },
        new()
        {
            Id = "mistral",
            Name = "Mistral (EU)",
            DefaultModel = "mistral-small-latest",
            ChatUrl = "https://api.mistral.ai/v1/chat/completions",
            KeyUrl = "https://console.mistral.ai/api-keys",
            Hint = "European company. Get a key at console.mistral.ai."
        },
        new()
        {
            Id = "deepseek",
            Name = "DeepSeek",
            DefaultModel = "deepseek-chat",
            ChatUrl = "https://api.deepseek.com/chat/completions",
            KeyUrl = "https://platform.deepseek.com/api_keys",
            Hint = "OpenAI-compatible and usually cheap. Key at platform.deepseek.com."
        },
        new()
        {
            Id = "xai",
            Name = "Grok (xAI)",
            DefaultModel = "grok-3-mini",
            ChatUrl = "https://api.x.ai/v1/chat/completions",
            KeyUrl = "https://console.x.ai/",
            Hint = "Grok via xAI’s OpenAI-compatible API. Key at console.x.ai."
        },
        new()
        {
            Id = "together",
            Name = "Together AI",
            DefaultModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo",
            ChatUrl = "https://api.together.ai/v1/chat/completions",
            KeyUrl = "https://api.together.ai/settings/api-keys",
            Hint = "Open-source models with an OpenAI-style key. together.ai"
        },
        new()
        {
            Id = "fireworks",
            Name = "Fireworks",
            DefaultModel = "accounts/fireworks/models/llama-v3p3-70b-instruct",
            ChatUrl = "https://api.fireworks.ai/inference/v1/chat/completions",
            KeyUrl = "https://fireworks.ai/account/api-keys",
            Hint = "Fast Llama and others. Key at fireworks.ai → API keys."
        },
        new()
        {
            Id = "cerebras",
            Name = "Cerebras (very fast)",
            DefaultModel = "llama-3.3-70b",
            ChatUrl = "https://api.cerebras.ai/v1/chat/completions",
            KeyUrl = "https://cloud.cerebras.ai/",
            Hint = "Very fast Llama inference. Key at cloud.cerebras.ai."
        },
        new()
        {
            Id = "venice",
            Name = "Venice (private inference)",
            DefaultModel = "venice-uncensored",
            ChatUrl = "https://api.venice.ai/api/v1/chat/completions",
            KeyUrl = "https://venice.ai/settings/api",
            Hint = "Privacy-focused OpenAI-compatible API. No Google. Key at venice.ai."
        },
        new()
        {
            Id = "perplexity",
            Name = "Perplexity (web-backed)",
            DefaultModel = "sonar",
            ChatUrl = "https://api.perplexity.ai/chat/completions",
            KeyUrl = "https://www.perplexity.ai/settings/api",
            Hint = "Answers with web search. Useful for finding replacements. Key in Perplexity API settings."
        },
        new()
        {
            Id = "compatible",
            Name = "Compatible (Ollama, OpenClaw, LM Studio…)",
            DefaultModel = "llama3.2",
            NeedsEndpoint = true,
            AllowMissingKey = true,
            KeyUrl = "https://github.com/ollama/ollama",
            Hint = "Any OpenAI-style /v1/chat/completions URL. Ollama: http://127.0.0.1:11434/v1  ·  LM Studio: http://127.0.0.1:1234/v1  ·  OpenClaw/LiteLLM: paste the gateway URL. Local URLs do not need a real key."
        }
    ];

    private static readonly HashSet<string> RetiredGroqModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "llama-3.3-70b-versatile",
        "llama-3.3-70b",
        "llama-3.1-8b-instant",
        "llama-3.1-70b-versatile",
        "llama3-70b-8192",
        "llama3-8b-8192",
        "mixtral-8x7b-32768",
        "gemma2-9b-it",
        "gemma-7b-it"
    };

    public static AiProviderDef Find(string? id) =>
        All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static bool IsRetiredGroqModel(string? model) =>
        !string.IsNullOrWhiteSpace(model) && RetiredGroqModels.Contains(model.Trim());

    public static string NormalizeModel(string? provider, string? model)
    {
        var def = Find(provider);
        var m = (model ?? "").Trim();
        if (string.IsNullOrWhiteSpace(m)) return def.DefaultModel;
        if (m.Equals("gpt-oss-120b", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("groq/gpt-oss-120b", StringComparison.OrdinalIgnoreCase))
            return "openai/gpt-oss-120b";
        if (m.Equals("gpt-oss-20b", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("groq/gpt-oss-20b", StringComparison.OrdinalIgnoreCase))
            return "openai/gpt-oss-20b";
        if (def.Id == "groq" && IsRetiredGroqModel(m))
            return def.DefaultModel;
        return m;
    }

    public static bool IsGoogleBlocked(params string?[] values) =>
        values.Any(v =>
            !string.IsNullOrWhiteSpace(v) &&
            (v.Contains("google", StringComparison.OrdinalIgnoreCase) ||
             v.Contains("gemini", StringComparison.OrdinalIgnoreCase) ||
             v.Contains("generativelanguage", StringComparison.OrdinalIgnoreCase)));

    public static string NormalizeChatUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            return url + "/chat/completions";
        return url + "/v1/chat/completions";
    }

    public static bool IsLoopback(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
               || url.Contains("localhost", StringComparison.OrdinalIgnoreCase)
               || url.Contains("[::1]", StringComparison.OrdinalIgnoreCase);
    }
}
