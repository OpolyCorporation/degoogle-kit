using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class ServiceCatalog
{
    public static IReadOnlyList<GoogleServiceDef> All { get; } = Create();
    public static IReadOnlyList<GoogleServiceDef> V1 => All.Where(s => s.InV1).ToList();

    public static Destination Pick(GoogleServiceDef def, MigrationMode mode) =>
        mode switch
        {
            MigrationMode.Privacy => def.Privacy,
            MigrationMode.Own => def.Own,
            _ => def.Easy
        };

    private static IReadOnlyList<GoogleServiceDef> Create() =>
    [
        S("gmail", "Gmail", "Mail", MigrationLevel.Easy, 12,
            "Proton Easy Switch moves mail, labels, contacts and calendars with Google’s permission. We deep-link; we do not take your Google password.",
            D("Proton Mail", "https://account.proton.me/mail/easy-switch", "Showcase migration"),
            D("Proton Mail", "https://account.proton.me/mail/easy-switch", "Encrypted"),
            D("Self-hosted mail / Nextcloud", "https://nextcloud.com/", "Own the stack"),
            "Mail/*.mbox"),
        S("calendar", "Google Calendar", "Mail", MigrationLevel.Easy, 6,
            "Easy Switch or import .ics from Takeout.",
            D("Proton Calendar", "https://account.proton.me/mail/easy-switch", ""),
            D("Proton Calendar", "https://account.proton.me/mail/easy-switch", ""),
            D("Nextcloud Calendar", "https://nextcloud.com/", "CalDAV"),
            "Calendar/*.ics"),
        S("contacts", "Google Contacts", "Mail", MigrationLevel.Easy, 6,
            "Easy Switch or import .vcf from Takeout.",
            D("Proton Contacts", "https://account.proton.me/mail/easy-switch", ""),
            D("Proton Contacts", "https://account.proton.me/mail/easy-switch", ""),
            D("Nextcloud Contacts", "https://nextcloud.com/", "CardDAV"),
            "Contacts/*.vcf"),
        S("drive", "Google Drive", "Files", MigrationLevel.Easy, 10,
            "Takeout keeps folders. Upload to Proton Drive or Nextcloud. Do not delete Drive until counts match.",
            D("Proton Drive", "https://proton.me/drive", "Preserve folders"),
            D("Proton Drive", "https://proton.me/drive", "Encrypted"),
            D("Nextcloud", "https://nextcloud.com/", "Self-host"),
            "Drive/"),
        S("docs", "Google Docs", "Files", MigrationLevel.Easy, 4,
            "Takeout Docs as .docx, then open in Proton Docs or ONLYOFFICE.",
            D("Proton Docs", "https://proton.me/drive", ".docx"),
            D("Proton Docs", "https://proton.me/drive", ""),
            D("ONLYOFFICE / LibreOffice", "https://www.onlyoffice.com/", ""),
            "Drive/*.docx"),
        S("sheets", "Google Sheets", "Files", MigrationLevel.Easy, 3,
            "Takeout as .xlsx / CSV, import to Proton Sheets.",
            D("Proton Sheets", "https://proton.me/drive", ".xlsx"),
            D("Proton Sheets", "https://proton.me/drive", ""),
            D("LibreOffice Calc", "https://www.libreoffice.org/", ""),
            "Drive/*.xlsx"),
        S("photos", "Google Photos", "Media", MigrationLevel.Easy, 12,
            "Takeout ZIP → Ente desktop importer, or Immich + immich-go if you self-host. Ente deduplicates exact copies.",
            D("Ente Photos", "https://ente.io/", "Recommended, encrypted, hosted"),
            D("Ente Photos", "https://ente.io/", "E2EE"),
            D("Immich", "https://immich.app/", "Self-host; use immich-go"),
            "Google Photos/"),
        S("keep", "Google Keep", "Notes", MigrationLevel.Converter, 4,
            "We convert Takeout Keep JSON to Markdown locally. Import that into Standard Notes or Joplin.",
            D("Standard Notes", "https://standardnotes.com/", "Recommended"),
            D("Standard Notes", "https://standardnotes.com/", ""),
            D("Joplin", "https://joplinapp.org/", "Advanced"),
            "Keep/*.json"),
        S("passwords", "Google Password Manager", "Identity", MigrationLevel.Easy, 10,
            "Chrome exports passwords.csv. Import into Proton Pass or Bitwarden, then delete the CSV. It is plaintext.",
            D("Proton Pass", "https://proton.me/pass", "CSV import"),
            D("Proton Pass", "https://proton.me/pass", ""),
            D("Bitwarden", "https://bitwarden.com/", "Also imports Chrome CSV"),
            "Chrome/Passwords.csv"),
        S("auth", "Google Authenticator", "Identity", MigrationLevel.Easy, 5,
            "Export transfer QR / codes, import Ente Auth or Aegis *before* you lose the phone.",
            D("Ente Auth", "https://ente.io/auth/", ""),
            D("Ente Auth", "https://ente.io/auth/", ""),
            D("Aegis", "https://getaegis.app/", "Offline Android"),
            ""),
        S("chrome", "Chrome", "Browser", MigrationLevel.Easy, 10,
            "Easy = Brave (still Chromium-like). Stronger = Firefox (less Google). Import bookmarks, history, passwords, extensions.",
            D("Brave", "https://brave.com/", "Almost identical to Chrome"),
            D("Firefox", "https://www.mozilla.org/firefox/", "Less Chromium/Google"),
            D("LibreWolf", "https://librewolf.net/", "Hardened Firefox"),
            "Chrome/"),
        S("search", "Google Search", "Browser", MigrationLevel.Easy, 3,
            "No personal data to move. Set default search in the new browser.",
            D("Brave Search", "https://search.brave.com/", "Recommended free"),
            D("DuckDuckGo / Kagi / Startpage", "https://duckduckgo.com/", ""),
            D("SearXNG", "https://searxng.org/", "Self-host"),
            ""),
        S("maps", "Google Maps saved places", "Maps", MigrationLevel.Converter, 5,
            "We turn Takeout saved places into GPX, KML, GeoJSON and CSV for Organic Maps / OsmAnd / HERE.",
            D("Organic Maps", "https://organicmaps.app/", "Everyday"),
            D("Organic Maps", "https://organicmaps.app/", ""),
            D("OsmAnd", "https://osmand.net/", "Power user"),
            "Maps/"),
        S("youtube", "YouTube", "Video", MigrationLevel.Limited, 8,
            "No real 1:1 replacement (catalog, comments, TV, monetization). Choose: leave (PeerTube), reduce (FreeTube — still YouTube backend), or archive Takeout locally.",
            D("Archive + FreeTube", "https://freetubeapp.io/", "Google backend still used"),
            D("PeerTube / Vimeo / Odysee", "https://joinpeertube.org/", "Leave YouTube"),
            D("Local archive", "", "Subscriptions, playlists, history, uploads"),
            "YouTube and YouTube Music/",
            youtubeWarning: true),
        S("ytmusic", "YouTube Music", "Video", MigrationLevel.Easy, 4,
            "Playlists/likes transfer with TuneMyMusic to TIDAL, Spotify or Deezer. We open their wizard; we do not store your library.",
            D("TIDAL / Spotify / Deezer", "https://www.tunemymusic.com/", "TuneMyMusic"),
            D("TIDAL", "https://www.tunemymusic.com/", ""),
            D("Local files / Navidrome", "https://www.navidrome.org/", ""),
            "YouTube and YouTube Music/"),
        S("meet", "Google Meet", "Chat", MigrationLevel.Easy, 2,
            "No history to move. Change the meeting link to Jitsi.",
            D("Jitsi Meet", "https://meet.jit.si/", "No account required"),
            D("Jitsi Meet", "https://meet.jit.si/", ""),
            D("Self-hosted Jitsi / Element Call", "https://element.io/", ""),
            ""),
        S("dns", "Google Public DNS", "Network", MigrationLevel.Easy, 3,
            "8.8.8.8 still sees hostnames you resolve. Quad9 or Cloudflare. Reversible in Network.",
            D("Quad9", "https://quad9.net/", "9.9.9.9"),
            D("Quad9 / Mullvad DNS", "https://quad9.net/", ""),
            D("Unbound on your router", "https://www.nlnetlabs.nl/projects/unbound/", ""),
            ""),
        S("home", "Google Home / Nest", "Home", MigrationLevel.Converter, 4,
            "Home Assistant can talk to many Nest devices during a slow transition. Some Nest features still call Google.",
            D("Home Assistant", "https://www.home-assistant.io/", "DeGoogle my home"),
            D("Home Assistant", "https://www.home-assistant.io/", ""),
            D("Home Assistant + local voice", "https://www.home-assistant.io/voice_control/", "Assist"),
            "",
            v1: true),
        S("android", "Android / Play Services", "Phone", MigrationLevel.Converter, 8,
            "GrapheneOS on a supported Pixel: no Google by default, or sandboxed Play for leftover apps. Only show Graphene if you have a compatible Pixel.",
            D("GrapheneOS (Pixel)", "https://grapheneos.org/", "Strongest"),
            D("GrapheneOS", "https://grapheneos.org/", "No Play, or sandboxed Play"),
            D("GrapheneOS /e/OS + F-Droid", "https://grapheneos.org/", ""),
            "")
    ];

    private static GoogleServiceDef S(
        string id, string name, string cat, MigrationLevel level, int weight, string how,
        Destination easy, Destination privacy, Destination own, string takeout,
        bool v1 = true, bool youtubeWarning = false) =>
        new()
        {
            Id = id, GoogleName = name, Category = cat, Level = level, Weight = weight, How = how,
            Easy = easy, Privacy = privacy, Own = own, TakeoutHint = takeout, InV1 = v1,
            YoutubeBackendWarning = youtubeWarning
        };

    private static Destination D(string name, string url, string note) =>
        new() { Name = name, Url = url, Note = note };
}
