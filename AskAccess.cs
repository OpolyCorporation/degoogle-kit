using System.Windows;
using DeGoogleKit.Services;

namespace DeGoogleKit;

internal static class AskAccess
{
    public static bool For(Window owner, AccessKind kind, string? extra = null, bool automatic = false)
    {
        var spec = PermissionService.Describe(kind, extra);
        var remembered = PermissionService.Get(kind);
        if (spec.Rememberable)
        {
            if (remembered == true) return true;
            if (remembered == false && automatic) return false;
            if (kind == AccessKind.AccountCloud && PermissionService.SessionAccountCloud && remembered != false)
                return true;
        }

        var dialog = new PermissionDialog(spec) { Owner = owner.IsLoaded ? owner : null };
        var ok = dialog.ShowDialog() == true;
        if (kind == AccessKind.AccountCloud)
            PermissionService.SessionAccountCloud = ok;
        if (spec.Rememberable && dialog.Remember)
            PermissionService.Set(kind, ok);
        PrivacyStore.Log(ok ? "permission_allow" : "permission_deny",
            kind + (string.IsNullOrWhiteSpace(extra) ? "" : " " + extra));
        return ok;
    }
}
