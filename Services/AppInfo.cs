using System.Reflection;

namespace DeGoogleKit.Services;

public static class AppInfo
{
    public static Version Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 3, 0, 0);

    public static string VersionText
    {
        get
        {
            var v = Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
