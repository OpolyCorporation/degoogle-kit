using System.IO;
using System.Text;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

/// <summary>Pro-only printable / shareable exports (plan + cleanup checklist).</summary>
public static class ProExportService
{
    public static string WritePlanMarkdown(IReadOnlyList<PlanRow> plan, PlanState meta)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"degoogle-plan-{DateTime.Now:yyyyMMdd-HHmm}.md");
        var score = PlanStore.Score(plan);
        var sb = new StringBuilder();
        sb.AppendLine("# DeGoogle Kit — my plan");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTime.Now:u} on this PC. Not uploaded.");
        sb.AppendLine($"Mode: **{meta.Mode}** · Chrome: **{meta.ChromeChoice}** · YouTube: **{meta.YoutubePath}**");
        sb.AppendLine($"Progress: **{score.Percent}%** moved.");
        if (score.RemainingHigh.Count > 0)
            sb.AppendLine("Still open: " + string.Join(", ", score.RemainingHigh));
        sb.AppendLine();
        sb.AppendLine("| Status | Google | Destination | Notes |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var row in plan.Where(r => r.Selected))
        {
            var mark = row.Status switch
            {
                ServiceStatus.Complete or ServiceStatus.ReadyToDisconnect or ServiceStatus.Verified => "done",
                ServiceStatus.ExportRequired => "export",
                _ => "todo"
            };
            sb.Append("| ")
                .Append(mark)
                .Append(" | ")
                .Append(EscapeCell(row.Def.GoogleName))
                .Append(" | ")
                .Append(EscapeCell(row.Destination.Name))
                .Append(" | ")
                .Append(EscapeCell(row.StatusLabel))
                .AppendLine(" |");
        }
        sb.AppendLine();
        sb.AppendLine("_Edit statuses in DeGoogle Kit → Your plan. This file is a snapshot only._");
        File.WriteAllText(path, sb.ToString());
        PrivacyStore.Log("plan_markdown_export", path);
        return path;
    }

    public static string WriteCleanupChecklist(ScanSnapshot scan)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"degoogle-cleanup-{DateTime.Now:yyyyMMdd-HHmm}.md");
        var sb = new StringBuilder();
        sb.AppendLine("# DeGoogle Kit — PC cleanup checklist");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTime.Now:u} on this PC. Not uploaded.");
        sb.AppendLine("Use this as a printable punch-list. Uninstall and DNS steps stay **manual** — the app never auto-deletes Google.");
        sb.AppendLine();
        sb.AppendLine("## Installed Google software");
        if (scan.Apps.Count == 0)
            sb.AppendLine("- [ ] (none found in Add/Remove Programs — still check Chrome / Android / web accounts)");
        else
        {
            foreach (var app in scan.Apps)
                sb.AppendLine($"- [ ] Uninstall **{app.Name}** → prefer **{app.Replacement}**");
        }

        if (scan.GoogleFolders.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Folders to review");
            foreach (var folder in scan.GoogleFolders.Take(40))
                sb.AppendLine($"- [ ] Review `{folder}`");
        }

        if (scan.GoogleTasks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Scheduled tasks");
            foreach (var task in scan.GoogleTasks.Distinct().Take(30))
                sb.AppendLine($"- [ ] Disable / remove task `{task}`");
        }

        if (scan.StartupEntries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Startup");
            foreach (var entry in scan.StartupEntries.Distinct().Take(30))
                sb.AppendLine($"- [ ] Review startup `{entry}`");
        }

        if (scan.ChromeExtensions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Chrome extensions (Google-related)");
            foreach (var ext in scan.ChromeExtensions.Distinct().Take(40))
                sb.AppendLine($"- [ ] Remove or replace `{ext}`");
        }

        if (scan.SignedInEmails.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Chrome Google accounts signed in");
            foreach (var email in scan.SignedInEmails)
                sb.AppendLine($"- [ ] Sign out / remove `{email}` when replacements are ready");
        }

        sb.AppendLine();
        sb.AppendLine("## DNS");
        sb.AppendLine(scan.DnsLooksLikeGoogle
            ? "- [ ] Google Public DNS detected — use Pro → Network to switch to Quad9 / Cloudflare / AdGuard (reversible)"
            : "- [ ] DNS does not look like Google Public DNS — still review Network if you want a privacy resolver");
        foreach (var line in scan.DnsServers.Take(8))
            sb.AppendLine($"  - `{line}`");

        sb.AppendLine();
        sb.AppendLine("_Re-scan in DeGoogle Kit after big changes. Keep this file private — it may list apps from this PC._");
        File.WriteAllText(path, sb.ToString());
        PrivacyStore.Log("cleanup_checklist_export", path);
        return path;
    }

    private static string EscapeCell(string s) =>
        (s ?? "").Replace("|", "/", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
