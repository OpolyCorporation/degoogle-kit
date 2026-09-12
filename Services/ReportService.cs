using System.IO;
using System.Net;
using System.Text;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class ReportService
{
    public static string Write(ScanSnapshot scan, IReadOnlyList<GuideItem> guide, IReadOnlyList<TakeoutEntry>? takeout)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"degoogle-report-{DateTime.Now:yyyyMMdd-HHmm}.html");

        var done = guide.Count(g => g.IsDone);
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><meta charset='utf-8'><title>DeGoogle Kit report</title>");
        sb.AppendLine("<body style='font-family:Segoe UI,sans-serif;max-width:800px;margin:40px auto;background:#071422;color:#F4F7FC;padding:24px'>");
        sb.AppendLine("<h1 style='color:#3EE6A8'>DeGoogle Kit report</h1>");
        sb.AppendLine($"<p>Generated {WebUtility.HtmlEncode(DateTime.Now.ToString("u"))} on this PC. Not uploaded.</p>");
        sb.AppendLine($"<p>License: {WebUtility.HtmlEncode(LicenseService.StatusText)}</p>");
        sb.AppendLine($"<h2>Browser</h2><p>{WebUtility.HtmlEncode(scan.DefaultBrowser)}</p>");
        sb.AppendLine("<h2>DNS</h2><p>" + WebUtility.HtmlEncode(string.Join(", ", scan.DnsServers)) +
                      (scan.DnsLooksLikeGoogle ? " (Google Public DNS detected)" : "") + "</p>");
        sb.AppendLine("<h2>Google software</h2><ul>");
        foreach (var app in scan.Apps)
            sb.AppendLine($"<li>{WebUtility.HtmlEncode(app.Name)} — {WebUtility.HtmlEncode(app.Replacement)}</li>");
        if (scan.Apps.Count == 0) sb.AppendLine("<li>None found</li>");
        sb.AppendLine("</ul>");
        sb.AppendLine($"<h2>Guide</h2><p>{done}/{guide.Count} complete</p><ul>");
        foreach (var g in guide)
            sb.AppendLine($"<li>{(g.IsDone ? "✓" : "○")} {WebUtility.HtmlEncode(g.Title)}</li>");
        sb.AppendLine("</ul>");
        if (takeout is { Count: > 0 })
        {
            sb.AppendLine("<h2>Takeout</h2><ul>");
            foreach (var t in takeout)
                sb.AppendLine($"<li>{WebUtility.HtmlEncode(t.Name)} {WebUtility.HtmlEncode(t.SizeLabel)} — {WebUtility.HtmlEncode(t.Hint)}</li>");
            sb.AppendLine("</ul>");
        }
        sb.AppendLine("<p style='color:#8BA4C7'>This report may contain app names from your PC. Keep it private.</p></body>");
        File.WriteAllText(path, sb.ToString());
        PrivacyStore.Log("report_written", path);
        return path;
    }
}
