using DeGoogleKit.Licensing;
using Stripe;
using Stripe.Checkout;
using System.Text.Json;

LoadDotEnvWalk(Directory.GetCurrentDirectory());
LoadDotEnvWalk(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();

app.MapGet("/health", () => Results.Ok(new { ok = true, service = "degoogle-kit-license" }));

app.MapMethods("/v1/license", ["GET", "POST"], async (HttpRequest request, IConfiguration config) =>
{
    var apiKey = Environment.GetEnvironmentVariable("STRIPE_API_KEY") ?? config["Stripe:ApiKey"];
    var pem = Environment.GetEnvironmentVariable("LICENSE_SIGNING_KEY") ?? config["License:SigningKey"];
    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(pem))
        return Results.Json(new { error = "License API is missing STRIPE_API_KEY or LICENSE_SIGNING_KEY." }, statusCode: 503);

    var sessionId = request.Query["session_id"].ToString();
    if (string.IsNullOrWhiteSpace(sessionId) && request.HasFormContentType)
        sessionId = request.Form["session_id"].ToString();
    if (string.IsNullOrWhiteSpace(sessionId) && request.ContentLength > 0
        && string.Equals(request.ContentType, "application/json", StringComparison.OrdinalIgnoreCase))
    {
        using var doc = await JsonDocument.ParseAsync(request.Body);
        if (doc.RootElement.TryGetProperty("session_id", out var el))
            sessionId = el.GetString() ?? "";
    }

    sessionId = sessionId.Trim();
    if (sessionId.Length == 0 || !sessionId.StartsWith("cs_", StringComparison.Ordinal))
        return Results.BadRequest(new { error = "Pass a Stripe Checkout session id (cs_…)." });

    try
    {
        var stripe = new StripeClient(apiKey);
        var sessions = new SessionService(stripe);
        var session = await sessions.GetAsync(sessionId, new SessionGetOptions
        {
            Expand = ["line_items.data.price.product"]
        });
        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "That session is not paid yet.", status = session.PaymentStatus }, statusCode: 402);

        var (sku, seats) = ResolveSku(session, config);
        if (string.IsNullOrWhiteSpace(sku) || sku.StartsWith("cloud", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "This payment is not a DeGoogle Kit Pro license." }, statusCode: 400);

        var created = session.Created == default
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            : new DateTimeOffset(DateTime.SpecifyKind(session.Created, DateTimeKind.Utc)).ToUnixTimeSeconds();

        var ticket = LicenseTicket.Sign(new LicensePayload
        {
            V = 1,
            Sku = sku,
            Seats = seats,
            Sid = session.Id,
            Iat = created
        }, pem);

        return Results.Ok(new { key = ticket, sku, seats, email = session.CustomerDetails?.Email });
    }
    catch (StripeException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 400);
    }
});

app.MapPost("/webhook", async (HttpRequest request, IConfiguration config) =>
{
    var secret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? config["Stripe:WebhookSecret"];
    if (string.IsNullOrWhiteSpace(secret))
        return Results.Json(new { error = "STRIPE_WEBHOOK_SECRET is not set." }, statusCode: 503);

    string json;
    using (var reader = new StreamReader(request.Body))
        json = await reader.ReadToEndAsync();
    var signature = request.Headers["Stripe-Signature"].ToString();
    try
    {
        var stripeEvent = EventUtility.ConstructEvent(json, signature, secret, throwOnApiVersionMismatch: false);
        _ = stripeEvent.Type;
        return Results.Ok(new { received = true });
    }
    catch (StripeException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 400);
    }
});

app.Run();

static (string? Sku, int Seats) ResolveSku(Session session, IConfiguration config)
{
    if (TryMeta(session.Metadata, out var sku, out var seats))
        return (sku, seats);

    var lifetimePrice = Environment.GetEnvironmentVariable("STRIPE_PRICE_LIFETIME") ?? config["Stripe:PriceLifetime"];
    var familyPrice = Environment.GetEnvironmentVariable("STRIPE_PRICE_FAMILY") ?? config["Stripe:PriceFamily"];

    foreach (var item in session.LineItems?.Data ?? [])
    {
        var priceId = item.Price?.Id;
        if (!string.IsNullOrWhiteSpace(familyPrice) && priceId == familyPrice)
            return ("family", 3);
        if (!string.IsNullOrWhiteSpace(lifetimePrice) && priceId == lifetimePrice)
            return ("lifetime", 1);

        if (item.Price?.Product is Product product && TryMeta(product.Metadata, out sku, out seats))
            return (sku, seats);
    }

    return (null, 0);
}

static bool TryMeta(Dictionary<string, string>? metadata, out string sku, out int seats)
{
    sku = "";
    seats = 1;
    if (metadata is null || !metadata.TryGetValue("sku", out var raw) || string.IsNullOrWhiteSpace(raw))
        return false;
    sku = raw.Trim();
    if (metadata.TryGetValue("seats", out var seatText) && int.TryParse(seatText, out var n))
        seats = n;
    if (sku.Equals("family", StringComparison.OrdinalIgnoreCase))
        seats = Math.Max(seats, 3);
    return true;
}

static void LoadDotEnvWalk(string start)
{
    var dir = start;
    for (var i = 0; i < 6 && !string.IsNullOrWhiteSpace(dir); i++)
    {
        LoadDotEnv(Path.Combine(dir, ".env"));
        dir = Directory.GetParent(dir)?.FullName ?? "";
    }
}

static void LoadDotEnv(string path)
{
    if (!System.IO.File.Exists(path)) return;
    foreach (var raw in System.IO.File.ReadAllLines(path))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var eq = line.IndexOf('=');
        if (eq < 1) continue;
        var key = line[..eq].Trim();
        var val = line[(eq + 1)..].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            Environment.SetEnvironmentVariable(key, val);
    }
}
