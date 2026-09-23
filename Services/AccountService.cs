using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DeGoogleKit.Services;

public sealed class AccountSession
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string Email { get; set; } = "";
    public string UserId { get; set; } = "";
}

public static class AccountService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static bool IsConfigured =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(AnonKey);

    public static string Url
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("DGK_SUPABASE_URL")?.Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env.TrimEnd('/');
            var custom = PrivacyStore.Settings.SupabaseUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(custom)) return custom.TrimEnd('/');
            return (ReadFile().Url ?? "").Trim().TrimEnd('/');
        }
    }

    public static string AnonKey
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("DGK_SUPABASE_ANON_KEY")?.Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env;
            var custom = PrivacyStore.Settings.SupabaseAnonKey?.Trim();
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            return (ReadFile().AnonKey ?? "").Trim();
        }
    }

    public static bool IsSignedIn => !string.IsNullOrWhiteSpace(Session?.AccessToken);

    public static AccountSession? Session => AccountStore.Load();

    public static string StatusText
    {
        get
        {
            if (!IsConfigured) return "Account cloud is not connected.";
            var s = Session;
            return s is null || string.IsNullOrWhiteSpace(s.Email)
                ? "Signed out. A free account backs up your plan and checklist on any PC. Paying is optional."
                : "Signed in as " + s.Email + ". Free: plan and checklist sync. Pro follows this account if you paid.";
        }
    }

    public static void SaveProject(string url, string anonKey)
    {
        var settings = PrivacyStore.Settings;
        settings.SupabaseUrl = (url ?? "").Trim().TrimEnd('/');
        settings.SupabaseAnonKey = (anonKey ?? "").Trim();
        PrivacyStore.SaveSettings(settings);
        PrivacyStore.Log("supabase_project_saved", settings.SupabaseUrl);
    }

    public static async Task<(bool Ok, string Message)> SignUpAsync(string email, string password)
    {
        if (!PermissionService.AccountCloudGranted)
            return (false, "Permission to use Supabase was not granted.");
        var check = Validate(email, password);
        if (check is not null) return (false, check);
        using var doc = await PostAuthAsync("/auth/v1/signup", new { email = email.Trim(), password });
        if (doc is null) return (false, "Could not reach Supabase Auth.");
        if (TryError(doc.RootElement, out var err)) return (false, err);
        if (TrySession(doc.RootElement, out var session))
        {
            AccountStore.Save(session);
            return (true, "Account created. You are signed in. Your plan and checklist will sync on this free account.");
        }
        return (true, "Account created. Confirm the email if Supabase asked, then sign in.");
    }

    public static async Task<(bool Ok, string Message)> SignInAsync(string email, string password)
    {
        if (!PermissionService.AccountCloudGranted)
            return (false, "Permission to use Supabase was not granted.");
        var check = Validate(email, password);
        if (check is not null) return (false, check);
        using var doc = await PostAuthAsync("/auth/v1/token?grant_type=password",
            new { email = email.Trim(), password });
        if (doc is null) return (false, "Could not reach Supabase Auth.");
        if (TryError(doc.RootElement, out var err)) return (false, err);
        if (!TrySession(doc.RootElement, out var session))
            return (false, "Sign-in did not return a session.");
        AccountStore.Save(session);
        return (true, "Signed in. Restoring your free plan backup.");
    }

    public static async Task SignOutAsync()
    {
        var s = Session;
        if (s is not null && !string.IsNullOrWhiteSpace(s.AccessToken))
        {
            try
            {
                using var http = CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, Url + "/auth/v1/logout");
                ApplyAnon(req);
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + s.AccessToken);
                await http.SendAsync(req);
            }
            catch
            {
                // Local sign-out still happens.
            }
        }
        AccountStore.Clear();
        PrivacyStore.Log("account_sign_out", "");
    }

    public static async Task<bool> TouchKeepaliveAsync()
    {
        if (!IsConfigured || !PermissionService.AccountCloudGranted) return false;
        try
        {
            using var http = CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, Url + "/rest/v1/rpc/touch_keepalive");
            ApplyAnon(req);
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                PrivacyStore.Log("supabase_keepalive", DateTime.UtcNow.ToString("u"));
                return true;
            }
        }
        catch
        {
            // Free-plan pause prevention is best-effort.
        }
        return false;
    }

    public static async Task<(bool Ok, string Message, string? Key)> PullLicenseAsync()
    {
        if (!PermissionService.AccountCloudGranted)
            return (false, "Permission to use Supabase was not granted.", null);
        var token = await ValidAccessTokenAsync();
        if (token is null) return (false, "Sign in first.", null);
        try
        {
            using var http = CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get,
                Url + "/rest/v1/licenses?select=license_key,sku,seats&limit=1");
            ApplyUser(req, token);
            using var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                return (false, "Could not load the account license.", null);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "[]" : body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return (true, "No paid license is linked to this account yet.", null);
            var row = doc.RootElement[0];
            var key = row.TryGetProperty("license_key", out var k) ? k.GetString() : null;
            if (string.IsNullOrWhiteSpace(key)) return (true, "Account license was empty.", null);
            return (true, "Paid license loaded from your account.", key);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public static async Task<(bool Ok, string Message)> BindLicenseAsync(string key)
    {
        key = (key ?? "").Trim();
        if (!key.StartsWith("DGK2.", StringComparison.Ordinal))
            return (false, "Only signed DGK2 keys can be linked to an account.");
        var token = await ValidAccessTokenAsync();
        if (token is null) return (false, "Sign in to copy Pro onto this account.");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DeGoogleKit/" + AppInfo.VersionText);
            using var req = new HttpRequestMessage(HttpMethod.Post, StripeStore.LicenseApiUrl + "/v1/account/bind");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { key }),
                System.Text.Encoding.UTF8,
                "application/json");
            using var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (!res.IsSuccessStatusCode)
            {
                var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : body;
                return (false, err ?? "Could not link the license to this account.");
            }
            return (true, "Pro is linked to this account. Sign in on another PC to use it.");
        }
        catch (HttpRequestException)
        {
            return (false, "License server is not reachable, so the key stayed on this PC only.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<(bool Ok, string Message, CloudProgress? Data)> PullProgressAsync()
    {
        if (!PermissionService.AccountCloudGranted)
            return (false, "Permission to use Supabase was not granted.", null);
        var token = await ValidAccessTokenAsync();
        if (token is null) return (false, "Sign in first.", null);
        try
        {
            using var http = CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get,
                Url + "/rest/v1/progress?select=guide_done,plan,updated_at,trial_started_at&limit=1");
            ApplyUser(req, token);
            using var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                return (false, ProgressError(body, "Could not load the free account backup. Run the latest supabase/schema.sql (progress table)."), null);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "[]" : body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return (true, "No cloud backup yet.", null);
            if (!TryReadProgress(doc.RootElement[0], out var row))
                return (true, "Cloud backup was empty.", null);
            return (true, "Loaded your free account backup.", row);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public static async Task<(bool Ok, string Message)> PushProgressAsync(IEnumerable<string> guideDone, PlanState plan)
    {
        if (!PermissionService.AccountCloudGranted)
            return (false, "Permission to use Supabase was not granted.");
        var token = await ValidAccessTokenAsync();
        if (token is null) return (false, "Sign in first.");
        var userId = Session?.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            userId = await FetchUserIdAsync(token);
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "Account user id missing. Sign in again.");
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["guide_done"] = guideDone.ToList(),
                ["plan"] = plan,
                ["updated_at"] = DateTime.UtcNow
            };
            using var http = CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, Url + "/rest/v1/progress?on_conflict=user_id");
            ApplyUser(req, token);
            req.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
            req.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json");
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                PrivacyStore.Log("progress_pushed", Session?.Email ?? "");
                return (true, "Plan and checklist saved to your free account.");
            }
            var body = await res.Content.ReadAsStringAsync();
            return (false, ProgressError(body, "Could not save the free account backup."));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<(bool Ok, string Message)> DeleteAccountAsync()
    {
        if (!PermissionService.AccountCloudGranted)
            return (false, "Permission to use Supabase was not granted.");
        var token = await ValidAccessTokenAsync();
        if (token is null) return (false, "Sign in first.");
        try
        {
            // Same RPC as the website account page (Art. 17) — clears support messages,
            // household invite rows, then auth.users (cascades progress/licenses).
            using var http = CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, Url + "/rest/v1/rpc/delete_my_account");
            ApplyUser(req, token);
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                AccountStore.Clear();
                PrivacyStore.Log("account_deleted", "rpc");
                return (true,
                    "Cloud account deleted on the shared database (same as the website): email, plan backup, linked license, household invites, and support messages. This PC still has local files until you erase them.");
            }
            var body = await res.Content.ReadAsStringAsync();
            return (false, ProgressError(body, "Could not delete the cloud account. Try signing in again, or use Delete my account on the website."));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// GDPR Art. 15/20 — same export_my_data RPC as the website Download my data button.
    /// </summary>
    public static async Task<(bool Ok, string Message, string? Json)> ExportMyDataAsync()
    {
        if (!PermissionService.AccountCloudGranted)
            return (false, "Permission to use Supabase was not granted.", null);
        var token = await ValidAccessTokenAsync();
        if (token is null) return (false, "Sign in first to export cloud account data.", null);
        try
        {
            using var http = CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, Url + "/rest/v1/rpc/export_my_data");
            ApplyUser(req, token);
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                return (false, ProgressError(body, "Could not export cloud account data."), null);
            if (string.IsNullOrWhiteSpace(body) || body.Trim() == "null")
                return (false, "Export returned empty data.", null);
            // Pretty-print when possible.
            try
            {
                using var doc = JsonDocument.Parse(body);
                body = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                // Keep raw body.
            }
            PrivacyStore.Log("account_export_rpc", Session?.Email ?? "");
            return (true, "Cloud account export ready (same data as the website).", body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public static async Task WriteExportSnapshotAsync()
    {
        if (!IsSignedIn) return;
        AppPaths.EnsureRoot();
        var export = await ExportMyDataAsync();
        if (export.Ok && !string.IsNullOrWhiteSpace(export.Json))
        {
            File.WriteAllText(Path.Combine(AppPaths.Root, "account-cloud-export.json"), export.Json);
            // Keep a small companion for older zip readers.
            File.WriteAllText(
                Path.Combine(AppPaths.Root, "account-cloud.json"),
                JsonSerializer.Serialize(new
                {
                    source = "export_my_data",
                    email = Session?.Email,
                    user_id = Session?.UserId,
                    exported_at = DateTime.UtcNow,
                    note = "Full account JSON is in account-cloud-export.json (same as website Download my data)."
                }, JsonFile.Options));
            return;
        }

        // Fallback if the RPC is missing on an older project: progress-only snapshot.
        var pull = await PullProgressAsync();
        var payload = new
        {
            email = Session?.Email,
            user_id = Session?.UserId,
            exported_at = DateTime.UtcNow,
            guide_done = pull.Data?.GuideDone,
            plan = pull.Data?.Plan,
            trial_started_at = pull.Data?.TrialStartedAt,
            updated_at = pull.Data?.UpdatedAt,
            warning = export.Message
        };
        File.WriteAllText(
            Path.Combine(AppPaths.Root, "account-cloud.json"),
            JsonSerializer.Serialize(payload, JsonFile.Options));
    }

    public static async Task<string?> ValidAccessTokenAsync()
    {
        var s = Session;
        if (s is null || string.IsNullOrWhiteSpace(s.RefreshToken)) return null;
        if (!string.IsNullOrWhiteSpace(s.AccessToken) && s.ExpiresAt > DateTime.UtcNow.AddMinutes(1))
            return s.AccessToken;

        using var doc = await PostAuthAsync("/auth/v1/token?grant_type=refresh_token",
            new { refresh_token = s.RefreshToken });
        if (doc is null || TryError(doc.RootElement, out _)) return null;
        if (!TrySession(doc.RootElement, out var next)) return null;
        if (string.IsNullOrWhiteSpace(next.Email)) next.Email = s.Email;
        if (string.IsNullOrWhiteSpace(next.UserId)) next.UserId = s.UserId;
        AccountStore.Save(next);
        return next.AccessToken;
    }

    private static async Task<string?> FetchUserIdAsync(string token)
    {
        try
        {
            using var http = CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, Url + "/auth/v1/user");
            ApplyUser(req, token);
            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var id = doc.RootElement.TryGetProperty("id", out var p) ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) return null;
            var s = Session;
            if (s is not null)
            {
                s.UserId = id;
                AccountStore.Save(s);
            }
            return id;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadProgress(JsonElement row, out CloudProgress progress)
    {
        progress = new CloudProgress();
        if (row.TryGetProperty("guide_done", out var done) && done.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in done.EnumerateArray())
            {
                var id = item.GetString();
                if (!string.IsNullOrWhiteSpace(id)) progress.GuideDone.Add(id);
            }
        }
        if (row.TryGetProperty("plan", out var plan) && plan.ValueKind == JsonValueKind.Object)
            progress.Plan = JsonSerializer.Deserialize<PlanState>(plan.GetRawText(), JsonFile.Options) ?? new PlanState();
        if (row.TryGetProperty("updated_at", out var at) && DateTimeOffset.TryParse(at.GetString(), out var stamp))
            progress.UpdatedAt = stamp;
        if (row.TryGetProperty("trial_started_at", out var trial) && trial.ValueKind != JsonValueKind.Null)
        {
            if (trial.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(trial.GetString(), out var started))
                progress.TrialStartedAt = started;
            else if (trial.TryGetDateTimeOffset(out var dto))
                progress.TrialStartedAt = dto;
        }
        return true;
    }

    public static async Task<(bool Ok, string Message, DateTimeOffset? StartedAt)> ClaimTrialAsync(
        IEnumerable<string> guideDone, PlanState plan)
    {
        if (!PermissionService.AccountCloudGranted)
            return (false, "Permission to use Supabase was not granted.", null);
        var token = await ValidAccessTokenAsync();
        if (token is null) return (false, "Sign in first.", null);
        var userId = Session?.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            userId = await FetchUserIdAsync(token);
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "Account user id missing. Sign in again.", null);

        var pull = await PullProgressAsync();
        if (!pull.Ok) return (false, pull.Message, null);
        if (pull.Data?.TrialStartedAt is { } existing)
            return (false, "This account already used the Pro trial.", existing);

        var started = DateTime.UtcNow;
        try
        {
            using var http = CreateClient();
            if (pull.Data is null)
            {
                var insert = new Dictionary<string, object?>
                {
                    ["user_id"] = userId,
                    ["guide_done"] = guideDone.ToList(),
                    ["plan"] = plan,
                    ["updated_at"] = started,
                    ["trial_started_at"] = started
                };
                using var req = new HttpRequestMessage(HttpMethod.Post, Url + "/rest/v1/progress");
                ApplyUser(req, token);
                req.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                req.Content = new StringContent(
                    JsonSerializer.Serialize(insert),
                    System.Text.Encoding.UTF8,
                    "application/json");
                using var res = await http.SendAsync(req);
                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                    return (false, ProgressError(body, "Could not save the trial to your account."), null);
            }
            else
            {
                using var req = new HttpRequestMessage(HttpMethod.Patch,
                    Url + "/rest/v1/progress?user_id=eq." + userId + "&trial_started_at=is.null");
                ApplyUser(req, token);
                req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
                req.Content = new StringContent(
                    JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["trial_started_at"] = started,
                        ["updated_at"] = started
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json");
                using var res = await http.SendAsync(req);
                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                    return (false, ProgressError(body, "Could not save the trial to your account."), null);
                if (LooksEmptyJson(body))
                {
                    var again = await PullProgressAsync();
                    if (again.Data?.TrialStartedAt is { } raced)
                        return (false, "This account already used the Pro trial.", raced);
                    return (false, "Could not save the trial to your account.", null);
                }
            }

            var saved = await PullProgressAsync();
            if (saved.Data?.TrialStartedAt is { } at)
                return (true, "Trial saved to this account.", at);
            return (true, "Trial saved to this account.", started);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    private static bool LooksEmptyJson(string body)
    {
        var t = (body ?? "").Trim();
        return t.Length == 0 || t == "[]" || t == "{}";
    }

    private static string ProgressError(string body, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.TryGetProperty("message", out var m) && !string.IsNullOrWhiteSpace(m.GetString()))
                return m.GetString()!;
        }
        catch
        {
            // Use the fallback.
        }
        return fallback;
    }

    private static string? Validate(string email, string password)
    {
        if (!IsConfigured) return "Add your Supabase project URL and anon key first.";
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
            return "Enter a valid email.";
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return "Password must be at least 8 characters.";
        return null;
    }

    private static async Task<JsonDocument?> PostAuthAsync(string path, object payload)
    {
        if (!IsConfigured) return null;
        try
        {
            using var http = CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, Url + path);
            ApplyAnon(req);
            req.Content = new StringContent(JsonSerializer.Serialize(payload, Json), System.Text.Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySession(JsonElement root, out AccountSession session)
    {
        session = new AccountSession();
        if (!root.TryGetProperty("access_token", out var at) || string.IsNullOrWhiteSpace(at.GetString()))
            return false;
        session.AccessToken = at.GetString()!;
        session.RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
        var seconds = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var n) ? n : 3600;
        session.ExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds));
        if (root.TryGetProperty("user", out var user))
        {
            session.Email = user.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "";
            session.UserId = user.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
        }
        return true;
    }

    private static bool TryError(JsonElement root, out string message)
    {
        message = "";
        if (root.TryGetProperty("error_description", out var d) && !string.IsNullOrWhiteSpace(d.GetString()))
        {
            message = d.GetString()!;
            return true;
        }
        if (root.TryGetProperty("msg", out var m) && !string.IsNullOrWhiteSpace(m.GetString()))
        {
            message = m.GetString()!;
            return true;
        }
        if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
        {
            message = e.GetString()!;
            return true;
        }
        return false;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DeGoogleKit/" + AppInfo.VersionText);
        return http;
    }

    private static void ApplyAnon(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("apikey", AnonKey);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + AnonKey);
    }

    private static void ApplyUser(HttpRequestMessage req, string token)
    {
        req.Headers.TryAddWithoutValidation("apikey", AnonKey);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
    }

    private static (string? Url, string? AnonKey) ReadFile()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "cloud", "supabase.json"),
                     Path.Combine(AppContext.BaseDirectory, "supabase.json")
                 })
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
                var key = doc.RootElement.TryGetProperty("anonKey", out var k) ? k.GetString() : null;
                return (url, key);
            }
            catch
            {
                // Ignore a bad local file.
            }
        }
        return (null, null);
    }
}
