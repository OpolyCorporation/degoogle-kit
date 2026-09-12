namespace DeGoogleKit.Services;

public enum AccessKind
{
    PcScan,
    OpenLink,
    OpenSettings,
    OpenFolder,
    Clipboard,
    UpdateCheck,
    TakeoutRead,
    TakeoutConvert,
    DesktopExport,
    StoreSecret,
    Uninstall,
    DnsChange,
    EraseAppData,
    InstallUpdate,
    CloudAiSend,
    ChangeLicense
}

public sealed class PermissionGrants
{
    public bool? PcScan { get; set; }
    public bool? OpenLinks { get; set; }
    public bool? Clipboard { get; set; }
    public bool? UpdateCheck { get; set; }
    public bool? TakeoutRead { get; set; }
    public bool? DesktopExport { get; set; }
    public bool? StoreSecrets { get; set; }
}

public static class PermissionService
{
    public static PermissionGrants Grants => JsonFile.Load(AppPaths.Permissions, new PermissionGrants());

    public static void Set(AccessKind kind, bool allowed)
    {
        var g = Grants;
        switch (kind)
        {
            case AccessKind.PcScan: g.PcScan = allowed; break;
            case AccessKind.OpenLink:
            case AccessKind.OpenSettings:
            case AccessKind.OpenFolder:
                g.OpenLinks = allowed; break;
            case AccessKind.Clipboard: g.Clipboard = allowed; break;
            case AccessKind.UpdateCheck: g.UpdateCheck = allowed; break;
            case AccessKind.TakeoutRead: g.TakeoutRead = allowed; break;
            case AccessKind.DesktopExport: g.DesktopExport = allowed; break;
            case AccessKind.StoreSecret: g.StoreSecrets = allowed; break;
        }
        JsonFile.Save(AppPaths.Permissions, g);
        PrivacyStore.Log("permission_set", kind + "=" + allowed);
    }

    public static bool? Get(AccessKind kind) => kind switch
    {
        AccessKind.PcScan => Grants.PcScan,
        AccessKind.OpenLink or AccessKind.OpenSettings or AccessKind.OpenFolder => Grants.OpenLinks,
        AccessKind.Clipboard => Grants.Clipboard,
        AccessKind.UpdateCheck => Grants.UpdateCheck,
        AccessKind.TakeoutRead => Grants.TakeoutRead,
        AccessKind.DesktopExport => Grants.DesktopExport,
        AccessKind.StoreSecret => Grants.StoreSecrets,
        _ => null
    };

    public static PermissionSpec Describe(AccessKind kind, string? extra = null) => kind switch
    {
        AccessKind.PcScan => new(
            "Scan this PC",
            "DeGoogle Kit wants a read-only look at this computer so it can list Google software and your default browser.",
            "Installed programs (Add/Remove registry), default browser, Google folders under Program Files / AppData, scheduled task names, and this adapter’s DNS servers.",
            "Nothing is uninstalled, deleted, or changed.",
            Rememberable: true,
            ChangesSystem: false),
        AccessKind.OpenLink => new(
            "Open a website",
            extra is null
                ? "Open this link in your default browser?"
                : "Open this website in your default browser?\n\n" + extra,
            "Your browser. The site may see your IP address. We do not send DeGoogle Kit data with the click.",
            "A browser window/tab will open.",
            Rememberable: true,
            ChangesSystem: false),
        AccessKind.OpenSettings => new(
            "Open Windows Settings",
            extra is null ? "Open Windows Settings?" : "Open Windows Settings?\n\n" + extra,
            "The Settings app on this PC.",
            "Settings will open. We do not flip any switches for you.",
            Rememberable: true,
            ChangesSystem: false),
        AccessKind.OpenFolder => new(
            "Open a folder",
            extra is null ? "Open this folder in File Explorer?" : "Open this folder in File Explorer?\n\n" + extra,
            "File Explorer on this PC.",
            "A folder window will open. Nothing is deleted.",
            Rememberable: true,
            ChangesSystem: false),
        AccessKind.Clipboard => new(
            "Use the clipboard",
            extra is null ? "Copy text to the clipboard?" : extra,
            "Windows clipboard (whatever is copied can be pasted in other apps).",
            "The current clipboard contents will be replaced.",
            Rememberable: true,
            ChangesSystem: false),
        AccessKind.UpdateCheck => new(
            "Check for updates",
            "Contact the update feed to see if a newer DeGoogle Kit exists.",
            "An HTTPS request to the feed URL in Privacy (GitHub Releases by default — not Google). User-Agent includes the app version. No scan data is sent.",
            "Nothing is installed until you choose Now, Later, or Midnight.",
            Rememberable: true,
            ChangesSystem: false),
        AccessKind.TakeoutRead => new(
            "Read a Takeout archive",
            extra is null
                ? "Read this Google Takeout zip or folder on this PC?"
                : "Read this Google Takeout archive on this PC?\n\n" + extra,
            "The file or folder you chose. Parsing stays local. It may include mail headers, photo counts, and a password CSV if you exported one.",
            "Nothing is uploaded. Google is not contacted. Your plan may be updated with what we find.",
            Rememberable: true,
            ChangesSystem: false),
        AccessKind.TakeoutConvert => new(
            "Convert Takeout locally",
            "Write converted files (Keep notes, maps, YouTube OPML, password CSV if present) under AppData\\DeGoogleKit\\normalized.",
            "The Takeout archive already on this PC.",
            "New files will be created in your DeGoogle Kit folder. Google is not changed. A password CSV is plaintext — treat it as a secret.",
            Rememberable: false,
            ChangesSystem: true),
        AccessKind.DesktopExport => new(
            "Write a file to the Desktop",
            extra ?? "Save a file on your Desktop?",
            "Your Desktop folder.",
            "A new file will be created. It can include names of apps from this PC. It is not uploaded.",
            Rememberable: true,
            ChangesSystem: true),
        AccessKind.StoreSecret => new(
            "Store a secret on this PC",
            extra ?? "Save this secret for your Windows user?",
            "A DPAPI-encrypted blob in AppData\\DeGoogleKit, readable only by this Windows account.",
            "The previous saved key (if any) will be replaced. It is never sent to us or to Google by this save.",
            Rememberable: true,
            ChangesSystem: true),
        AccessKind.Uninstall => new(
            "Uninstall a program",
            extra ?? "Run the official Windows uninstaller?",
            "The uninstaller published by that program.",
            "The program may be removed. DeGoogle Kit does not delete Chrome/Drive profiles itself.",
            Rememberable: false,
            ChangesSystem: true),
        AccessKind.DnsChange => new(
            "Change DNS",
            extra ?? "Change this PC’s DNS servers?",
            "The active network adapter. Windows will ask for administrator permission (UAC).",
            "Name resolution will use the servers you confirmed. Your previous DNS is saved so it can be restored. The network may drop for a moment.",
            Rememberable: false,
            ChangesSystem: true),
        AccessKind.EraseAppData => new(
            "Erase DeGoogle Kit data",
            "Delete all DeGoogle Kit data on this PC (progress, license, keys, logs, conversions) and close the app?",
            "The folder AppData\\DeGoogleKit.",
            "That folder will be permanently deleted. Google accounts are not touched. This cannot be undone from the app.",
            Rememberable: false,
            ChangesSystem: true),
        AccessKind.InstallUpdate => new(
            "Install an update",
            extra ?? "Replace DeGoogle Kit files and restart?",
            "The app folder and a package already downloaded to AppData (HTTPS, checksum when the feed provides one).",
            "DeGoogle Kit will close, files will be overwritten, then the new version will start.",
            Rememberable: false,
            ChangesSystem: true),
        AccessKind.CloudAiSend => new(
            "Send a question to cloud AI",
            extra ?? "Send your question and a local scan summary to the provider you chose (not Google)?",
            "The internet, using your own API key. The provider’s servers will receive the question and a summary of this PC’s scan/guide.",
            "A network request leaves this PC. Withdraw consent anytime on the Coach tab.",
            Rememberable: false,
            ChangesSystem: false),
        AccessKind.ChangeLicense => new(
            "Change Pro / trial status",
            extra ?? "Change the license state stored on this PC?",
            "license.json in AppData\\DeGoogleKit.",
            "Trial or Pro status on this PC will change. No card is stored here.",
            Rememberable: false,
            ChangesSystem: true),
        _ => new("Permission", "Allow this action?", "This PC.", "", false, false)
    };

    public readonly record struct PermissionSpec(
        string Title,
        string Summary,
        string Access,
        string Change,
        bool Rememberable,
        bool ChangesSystem);
}
