using System.IO;

namespace DeGoogleKit.Services;

public static class ShortcutService
{
    public const string LinkName = "DeGoogle Kit.lnk";

    public static string? ExePath
    {
        get
        {
            var exe = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(exe) || !File.Exists(exe) ? null : exe;
        }
    }

    public static string DesktopLink => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LinkName);

    public static string StartMenuLink => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), LinkName);

    public static bool CreateDesktop() => Create(DesktopLink);
    public static bool CreateStartMenu() => Create(StartMenuLink);

    public static bool Create(string linkPath)
    {
        var exe = ExePath;
        if (exe is null) return false;
        try
        {
            var dir = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return false;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
            shortcut.WindowStyle = 1;
            shortcut.Description = "DeGoogle Kit";
            shortcut.IconLocation = exe + ",0";
            shortcut.Save();
            PrivacyStore.Log("shortcut_created", Path.GetFileName(linkPath));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
