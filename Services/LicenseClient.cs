using System.Net.Http;
using System.Text.Json;

namespace DeGoogleKit.Services;

public static class LicenseClient
{
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
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (!res.IsSuccessStatusCode)
            {
                var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : json;
                return (false, err ?? ("License server HTTP " + (int)res.StatusCode), null);
            }

            if (!doc.RootElement.TryGetProperty("key", out var keyEl))
                return (false, "License server did not return a key.", null);
            return (true, "License issued.", keyEl.GetString());
        }
        catch (HttpRequestException)
        {
            return (false, "Could not reach the license server at " + StripeStore.LicenseApiUrl +
                           ". Start license-api locally or set DGK_LICENSE_API to your live server.", null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }
}
