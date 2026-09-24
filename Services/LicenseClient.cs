using System.Net.Http;
using System.Text.Json;

namespace DeGoogleKit.Services;

public static class LicenseClient
{
    public static async Task<(bool Ok, string Message, string? Key)> RedeemAsync(string raw)
    {
        var token = ProtocolRegistration.ExtractToken(raw);
        if (string.IsNullOrWhiteSpace(token))
            token = (raw ?? "").Trim();

        if (token.StartsWith("DGK2.", StringComparison.Ordinal))
            return (true, "License key ready.", token);

        if (token.StartsWith("cs_", StringComparison.OrdinalIgnoreCase))
            return await ExchangeSessionAsync(token);

        return (false, "Paste a Stripe session id (cs_…) or a DGK2 license key.", null);
    }

    public static async Task<(bool Ok, string Message, string? Key)> ExchangeSessionAsync(string sessionId)
    {
        sessionId = sessionId.Trim();
        if (!sessionId.StartsWith("cs_", StringComparison.Ordinal))
            return (false, "Paste the Stripe Checkout session id. It starts with cs_.", null);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DeGoogleKit/" + AppInfo.VersionText);
            var url = StripeStore.LicenseApiUrl + "/v1/license?session_id=" + Uri.EscapeDataString(sessionId);
            using var res = await http.GetAsync(url);
            var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                // Prefer JSON error body when present; plain Unauthorized must not throw.
                if (LooksLikeJson(json))
                {
                    try
                    {
                        using var errDoc = JsonDocument.Parse(json);
                        if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                            errEl.GetString() is { Length: > 0 } err)
                            return (false, err, null);
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
                return (false, UserFacing.LicenseServerBusy(), null);
            }

            if (!LooksLikeJson(json))
                return (false, UserFacing.LicenseServerBusy(), null);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("key", out var keyEl))
                return (false, UserFacing.LicenseServerBusy(), null);
            var key = keyEl.GetString();
            if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("DGK2.", StringComparison.Ordinal))
                return (false, "That payment could not be turned into a license this app recognizes. Open a GitHub issue with subject “License”.", null);
            return (true, "License issued.", key);
        }
        catch (HttpRequestException)
        {
            return (false, UserFacing.LicenseRedeemOffline(), null);
        }
        catch (Exception)
        {
            return (false, UserFacing.LicenseRedeemOffline(), null);
        }
    }

    private static bool LooksLikeJson(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.TrimStart();
        return t.StartsWith('{') || t.StartsWith('[');
    }
}
