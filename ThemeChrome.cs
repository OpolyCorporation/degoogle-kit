using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DeGoogleKit;

internal static class ThemeChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaBorderColor = 34;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window window)
    {
        void Paint()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int dark = 1;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref dark, sizeof(int));

            // COLORREF is 0x00BBGGRR — navy #071422
            int navy = 0x00221407;
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref navy, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref navy, sizeof(int));
        }

        if (window.IsInitialized && new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Paint();
        else
            window.SourceInitialized += (_, _) => Paint();

        window.Opacity = 0;
        window.Loaded += (_, _) => Motion.FadeIn(window, 0, 300);
    }
}
