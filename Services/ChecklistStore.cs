using System.IO;
using System.Text.Json;
using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class ChecklistStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeGoogleKit",
        "progress.json");

    public static List<GuideItem> Load()
    {
        var items = GuideCatalog.Create();
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var done = JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
                foreach (var item in items)
                {
                    item.IsDone = done.Contains(item.Id);
                }
            }
        }
        catch
        {
            // Missing or corrupt progress is treated as a fresh start.
        }

        return items;
    }

    public static void Save(IEnumerable<GuideItem> items)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var done = items.Where(i => i.IsDone).Select(i => i.Id).ToHashSet();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(done, JsonOptions));
    }
}
