using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public sealed class CloudProgress
{
    public List<string> GuideDone { get; set; } = [];
    public PlanState Plan { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? TrialStartedAt { get; set; }
}

public static class ProgressCloud
{
    public static List<string> GuideIds(IEnumerable<GuideItem> items) =>
        items.Where(i => i.IsDone).Select(i => i.Id).ToList();

    public static int ApplyGuide(IList<GuideItem> items, IEnumerable<string> remoteDone)
    {
        var extra = 0;
        var set = remoteDone.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!item.IsDone && set.Contains(item.Id))
            {
                item.IsDone = true;
                extra++;
            }
        }
        return extra;
    }

    public static PlanState MergePlan(PlanState local, PlanState remote)
    {
        var merged = new PlanState
        {
            Mode = local.Mode,
            ChromeChoice = local.ChromeChoice,
            YoutubePath = local.YoutubePath,
            Selected = new Dictionary<string, bool>(local.Selected),
            Status = new Dictionary<string, string>(local.Status)
        };

        if (IsFresh(local) && !IsFresh(remote))
        {
            merged.Mode = remote.Mode;
            merged.ChromeChoice = remote.ChromeChoice;
            merged.YoutubePath = remote.YoutubePath;
        }

        foreach (var kv in remote.Selected)
        {
            if (!merged.Selected.TryGetValue(kv.Key, out var on) || (!on && kv.Value))
                merged.Selected[kv.Key] = kv.Value;
        }

        foreach (var kv in remote.Status)
        {
            if (!merged.Status.TryGetValue(kv.Key, out var cur) || Rank(kv.Value) > Rank(cur))
                merged.Status[kv.Key] = kv.Value;
        }

        return merged;
    }

    private static bool IsFresh(PlanState state) =>
        state.Status.Count == 0
        || state.Status.Values.All(v =>
            string.IsNullOrWhiteSpace(v) || v.Equals(nameof(ServiceStatus.NotStarted), StringComparison.OrdinalIgnoreCase));

    private static int Rank(string status)
    {
        if (!Enum.TryParse<ServiceStatus>(status, out var s)) return 0;
        return s switch
        {
            ServiceStatus.NotStarted => 0,
            ServiceStatus.GoogleStillConnected => 1,
            ServiceStatus.ExportRequired => 2,
            ServiceStatus.ReadyToImport => 3,
            ServiceStatus.Imported => 4,
            ServiceStatus.Verified => 5,
            ServiceStatus.ReadyToDisconnect => 6,
            ServiceStatus.Complete => 7,
            _ => 0
        };
    }
}
