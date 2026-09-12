using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public sealed class PlanState
{
    public string Mode { get; set; } = nameof(MigrationMode.Easy);
    public string ChromeChoice { get; set; } = "Brave";
    public string YoutubePath { get; set; } = "Reduce";
    public Dictionary<string, bool> Selected { get; set; } = [];
    public Dictionary<string, string> Status { get; set; } = [];
}

public static class PlanStore
{
    public static PlanState Load() => JsonFile.Load(AppPaths.Plan, new PlanState());

    public static void Save(PlanState state) => JsonFile.Save(AppPaths.Plan, state);

    public static MigrationMode ParseMode(string mode) =>
        Enum.TryParse<MigrationMode>(mode, out var m) ? m : MigrationMode.Easy;

    public static List<PlanRow> Rows(PlanState state)
    {
        var mode = ParseMode(state.Mode);
        var rows = new List<PlanRow>();
        foreach (var def in ServiceCatalog.V1)
        {
            var row = new PlanRow
            {
                Def = def,
                Destination = ChromeOverride(def, mode, state),
                Selected = !state.Selected.TryGetValue(def.Id, out var sel) || sel
            };
            if (state.Status.TryGetValue(def.Id, out var st) && Enum.TryParse<ServiceStatus>(st, out var parsed))
                row.Status = parsed;
            rows.Add(row);
        }
        return rows;
    }

    public static void Persist(IEnumerable<PlanRow> rows, PlanState meta)
    {
        meta.Selected = rows.ToDictionary(r => r.Def.Id, r => r.Selected);
        meta.Status = rows.ToDictionary(r => r.Def.Id, r => r.Status.ToString());
        Save(meta);
    }

    public static ScoreSnapshot Score(IEnumerable<PlanRow> rows)
    {
        var selected = rows.Where(r => r.Selected).ToList();
        var total = selected.Sum(r => r.Def.Weight);
        if (total == 0) return new ScoreSnapshot { Percent = 0, Remaining = 100 };
        static bool CountsAsMoved(ServiceStatus s) =>
            s is ServiceStatus.Complete or ServiceStatus.ReadyToDisconnect or ServiceStatus.Verified;
        var done = selected.Where(r => CountsAsMoved(r.Status)).Sum(r => r.Def.Weight);
        var percent = (int)Math.Round(100.0 * done / total);
        var remaining = selected
            .Where(r => !CountsAsMoved(r.Status))
            .OrderByDescending(r => r.Def.Weight)
            .Take(4)
            .Select(r => r.Def.GoogleName)
            .ToList();
        return new ScoreSnapshot { Percent = percent, Remaining = 100 - percent, RemainingHigh = remaining };
    }

    private static Destination ChromeOverride(GoogleServiceDef def, MigrationMode mode, PlanState state)
    {
        if (def.Id == "chrome")
        {
            return state.ChromeChoice.Equals("Firefox", StringComparison.OrdinalIgnoreCase)
                ? def.Privacy
                : def.Easy;
        }
        if (def.Id == "youtube")
        {
            return state.YoutubePath switch
            {
                "Leave" => def.Privacy,
                "Archive" => def.Own,
                _ => def.Easy
            };
        }
        return ServiceCatalog.Pick(def, mode);
    }
}
