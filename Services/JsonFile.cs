using System.IO;
using System.Text.Json;

namespace DeGoogleKit.Services;

internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Load<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static void Save<T>(string path, T value)
    {
        AppPaths.EnsureRoot();
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }
}
