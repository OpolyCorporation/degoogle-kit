using System.Windows;
using DeGoogleKit.Services;

namespace DeGoogleKit;

internal static class AskAccess
{
    public static bool For(Window owner, AccessKind kind, string? extra = null, bool automatic = false)
    {
        var spec = PermissionService.Describe(kind, extra);
        if (spec.Rememberable)
        {
            var remembered = PermissionService.Get(kind);
            if (remembered == true) return true;
            if (remembered == false && automatic) return false;
        }

        var dialog = new PermissionDialog(spec) { Owner = owner.IsLoaded ? owner : null };
        var ok = dialog.ShowDialog() == true;
        if (spec.Rememberable && dialog.Remember)
            PermissionService.Set(kind, ok);
        PrivacyStore.Log(ok ? "permission_allow" : "permission_deny",
            kind + (string.IsNullOrWhiteSpace(extra) ? "" : " " + extra));
        return ok;
    }
}
