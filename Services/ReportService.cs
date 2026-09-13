using System.IO;
using System.Net;
using System.Text;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class ReportService
{
    public static string Write(
        ScanSnapshot scan,
        IReadOnlyList<GuideItem> guide,
        IReadOnlyList<TakeoutEntry>? takeout,
        IReadOnlyList<PlanRow>? plan = null)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"degoogle-report-{DateTime.Now:yyyyMMdd-HHmm}.html");

        var done = guide.Count(g => g.IsDone);
        var score = plan is null ? null : PlanStore.Score(plan);
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><meta charset='utf-8'><title>DeGoogle Kit report</title>");
        sb.AppendLine("<body style='font-family:Segoe UI,sans-serif;max-width:800px;margin:40px auto;background:#071422;color:#F4F7FC;padding:24px'>");
        sb.AppendLine("<h1 style='color:#3EE6A8'>DeGoogle Kit report</h1>");
        sb.AppendLine($"<p>Generated {WebUtility.HtmlEncode(DateTime.Now.ToString("u"))} on this PC. Not uploaded.</p>");
        sb.AppendLine($"<p>License: {WebUtility.HtmlEncode(LicenseService.StatusText)}</p>");
        sb.AppendLine($"<h2>Browser</h2><p>{WebUtility.HtmlEncode(scan.DefaultBrowser)}</p>");
        sb.AppendLine("<h2>DNS</h2><p>" + WebUtility.HtmlEncode(string.Join("; ", scan.DnsServers)) +
                      (scan.DnsLooksLikeGoogle ? " (Google Public DNS detected)" : "") + "</p>");
        sb.AppendLine("<h2>Google software</h2><ul>");
        foreach (var app in scan.Apps)
            sb.AppendLine($"<li>{WebUtility.HtmlEncode(app.Name)} — {WebUtility.HtmlEncode(app.Replacement)}</li>");
        if (scan.Apps.Count == 0) sb.AppendLine("<li>None found in Add/Remove Programs</li>");
        sb.AppendLine("</ul>");
        if (scan.GoogleFolders.Count > 0)
        {
            sb.AppendLine("<h2>Folders</h2><ul>");
            foreach (var folder in scan.GoogleFolders)
                sb.AppendLine($"<li>{WebUtility.HtmlEncode(folder)}</li>");
            sb.AppendLine("</ul>");
        }
        if (scan.GoogleTasks.Count > 0)
        {
            sb.AppendLine("<h2>Scheduled tasks</h2><p>" + WebUtility.HtmlEncode(string.Join(", ", scan.GoogleTasks)) + "</p>");
        }
        if (scan.GoogleServices.Count > 0)
        {
            sb.AppendLine("<h2>Windows services</h2><p>" + WebUtility.HtmlEncode(string.Join(", ", scan.GoogleServices.Distinct().Take(30))) + "</p>");
        }
        if (scan.ChromeExtensions.Count > 0)
        {
            sb.AppendLine("<h2>Chrome extensions (Google-related)</h2><ul>");
            foreach (var ext in scan.ChromeExtensions.Distinct().Take(40))
                sb.AppendLine($"<li>{WebUtility.HtmlEncode(ext)}</li>");
            sb.AppendLine("</ul>");
        }
        if (scan.GoogleProcesses.Count > 0)
            sb.AppendLine("<h2>Running processes</h2><p>" + WebUtility.HtmlEncode(string.Join(", ", scan.GoogleProcesses.Distinct())) + "</p>");
        if (scan.StartupEntries.Count > 0)
            sb.AppendLine("<h2>Startup</h2><p>" + WebUtility.HtmlEncode(string.Join(", ", scan.StartupEntries.Distinct())) + "</p>");
        if (scan.SignedInEmails.Count > 0)
            sb.AppendLine("<h2>Chrome Google accounts</h2><p>" + WebUtility.HtmlEncode(string.Join(", ", scan.SignedInEmails)) + "</p>");
        if (score is not null)
            sb.AppendLine($"<h2>Plan score</h2><p>{score.Percent}% moved. Remaining: {WebUtility.HtmlEncode(string.Join(", ", score.RemainingHigh))}</p>");
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
