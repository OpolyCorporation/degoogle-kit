using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public sealed class OfflineTopic
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string[] Keys { get; init; }
    public required string Body { get; init; }
}

/// <summary>
/// Local, offline answers for leaving Google. Facts checked against public docs
/// (Takeout, Proton Easy Switch, Ente, GrapheneOS FAQ, Quad9, Organic Maps) as of 2026-09.
/// Nothing here is sent to a network.
/// </summary>
public static class OfflineGuide
{
    public static IReadOnlyList<OfflineTopic> Topics { get; } = Create();

    public static string Index()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Offline coach — these answers stay on this PC.");
        sb.AppendLine("Ask any title, or a product name. Order matters: export → replace → verify counts → then disconnect Google. This app never deletes Google for you.");
        sb.AppendLine();
        foreach (var t in Topics)
            sb.AppendLine("• " + t.Title);
        sb.AppendLine();
        sb.AppendLine("Also: “what should I do first?” uses a plan from this PC’s scan.");
        return sb.ToString();
    }

    public static string Corpus()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in Topics)
        {
            sb.AppendLine("## " + t.Title);
            sb.AppendLine(t.Body);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Answer(string question, ScanSnapshot scan, IReadOnlyList<GuideItem> guide)
    {
        var q = (question ?? "").Trim().ToLowerInvariant();
        if (q.Length == 0)
            return Index();

        if (IsIndexAsk(q))
            return Index();

        if (IsPlanAsk(q))
            return AiCoach.LocalPlan(scan, guide) + "\n\n" + ShortFooter();

        OfflineTopic? best = null;
        var bestScore = 0;
        foreach (var topic in Topics)
        {
            var score = Score(q, topic);
            if (score > bestScore)
            {
                bestScore = score;
                best = topic;
            }
        }

        if (best is not null && bestScore >= 2)
            return Format(best) + ScanNote(scan, best.Id);

        if (best is not null && bestScore == 1)
            return Format(best) + ScanNote(scan, best.Id) + "\n\nIf that was the wrong topic, ask “topics” for the full list.";

        return Index() + "\nI did not match a specific guide. Ask one of the titles above, or add your own AI key for a free-form answer (never Gemini).";
    }

    /// <summary>
    /// Compact context for hosted / BYOK models so we do not pay to send every guide every time.
    /// </summary>
    public static string ForHosted(string question, ScanSnapshot scan, IReadOnlyList<GuideItem> guide)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(AiCoach.LocalPlan(scan, guide));
        sb.AppendLine();
        sb.AppendLine("Guide titles: " + string.Join("; ", Topics.Select(t => t.Title)));
        var q = (question ?? "").Trim().ToLowerInvariant();
        var ranked = Topics
            .Select(t => (Topic: t, Score: Score(q, t)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(2)
            .ToList();
        if (ranked.Count == 0)
        {
            var order = Topics.FirstOrDefault(t => t.Id == "order");
            if (order is not null)
            {
                sb.AppendLine();
                sb.AppendLine("## " + order.Title);
                sb.AppendLine(order.Body);
            }
        }
        else
        {
            foreach (var (topic, _) in ranked)
            {
                sb.AppendLine();
                sb.AppendLine("## " + topic.Title);
                sb.AppendLine(topic.Body);
                var note = ScanNote(scan, topic.Id);
                if (note.Length > 0) sb.AppendLine(note.Trim());
            }
        }
        return sb.ToString();
    }

    private static string Format(OfflineTopic topic) =>
        topic.Title + "\n\n" + topic.Body + "\n\n" + ShortFooter();

    private static string ShortFooter() =>
        "DeGoogle Kit never auto-deletes Google. Verify counts on the new side before you disconnect or erase.";

    private static bool IsIndexAsk(string q) =>
        q is "topics" or "list" or "help" or "menu" or "index" or "guides"
        || q.Contains("what can you") || q.Contains("what do you know")
        || q.Contains("offline") && q.Contains("list")
        || q.Contains("emner") || q.Contains("oversigt");

    private static bool IsPlanAsk(string q) =>
        q is "plan" or "start" or "begin"
        || q.Contains("do first") || q.Contains("first step") || q.Contains("what should i do")
        || q.Contains("hvad skal jeg") || q.Contains("hvor begynder") || q.Contains("først")
        || (q.Contains("write") && q.Contains("plan"))
        || (q.Contains("personal") && q.Contains("plan"));

    private static int Score(string q, OfflineTopic topic)
    {
        var n = 0;
        if (q.Contains(topic.Id, StringComparison.Ordinal)) n += 4;
        foreach (var key in topic.Keys)
        {
            if (key.Length == 0) continue;
            if (q.Contains(key, StringComparison.Ordinal))
                n += key.Length >= 8 ? 3 : key.Length >= 5 ? 2 : 1;
        }
        return n;
    }

    private static string ScanNote(ScanSnapshot scan, string id)
    {
        return id switch
        {
            "chrome" when scan.DefaultBrowserIsGoogle =>
                "\n\nThis PC: Windows still opens links in Chrome. Switch the default browser before you uninstall.",
            "dns" when scan.DnsLooksLikeGoogle =>
                "\n\nThis PC: Google Public DNS is in use. Network tab can switch to Quad9 (Pro, reversible).",
            "chrome" when scan.SignedInEmails.Count > 0 =>
                "\n\nThis PC: Chrome still has a Google account signed in.",
            "drive" when scan.Apps.Any(a => a.Name.Contains("Drive", StringComparison.OrdinalIgnoreCase)) =>
                "\n\nThis PC: Google Drive for desktop is still installed. Uninstall from This PC after files are elsewhere.",
            _ => ""
        };
    }

    private static IReadOnlyList<OfflineTopic> Create() =>
    [
        T("order", "The safe order to leave Google",
            ["order", "sequence", "steps", "rækkefølge", "before delete", "don't delete", "do not delete"],
            """
            Do this in order. Skipping ahead is how people lock themselves out.

            1. Scan this PC (This PC tab) so you know what is installed.
            2. Request Google Takeout (takeout.google.com) and keep the zip on this PC. Exporting does not delete anything.
            3. Move 2FA off Google Authenticator (Ente Auth or Aegis) before you touch the phone.
            4. Export passwords (Chrome CSV or Takeout) into Proton Pass or Bitwarden, then delete the CSV. It is plaintext.
            5. Mail, calendar, contacts (Proton Easy Switch is the easy path).
            6. Photos (Ente desktop importer) and Drive files.
            7. Browser + search + DNS.
            8. Compare counts (mail, photos, files, places) on the new side.
            9. Only then disconnect products or delete the Google account. Last step, never first.

            Work/school Google Workspace is often not yours to delete. Personal Gmail is.
            """),

        T("takeout", "Google Takeout — export a copy first",
            ["takeout", "export", "download my data", "portability", "art. 20", "art 20"],
            """
            Takeout (takeout.google.com) is Google’s official copy of data they choose to include. It is a copy, not a delete.

            Facts: you pick products (50+ exist: Gmail, Drive, Photos, YouTube, Chrome, Maps, Keep, Fit, Pay, Gemini chats, Timeline, and more). Default is often “everything selected” — deselect if you only need Photos/Mail. Format: ZIP is the practical choice. Large libraries split into several zips. The download link is time-limited (on the order of about a week, with a limited number of tries) — save it locally.

            What Takeout is not: it is not every log Google ever made, not deleted items, not a full Workspace admin export, and not a guarantee that another app can import every file.

            In this app: drop the zip or folder on Drop Takeout. We count mail/photos/places locally, warn if a Chrome password CSV is inside, and can convert Keep → Markdown and Maps → GPX/KML/GeoJSON/CSV. Nothing is uploaded.

            Next: import into the replacement, compare counts, then consider disconnecting Google.
            """),

        T("gmail", "Leave Gmail",
            ["gmail", "email", "e-mail", "inbox", "proton mail", "tuta", "fastmail", "easy switch", "mail"],
            """
            Easy path: Proton Mail Easy Switch (account.proton.me → Import via Easy Switch → Google). That is Proton’s OAuth with Google — this app never takes your Google password. It can import mail (with attachments), calendars, and contacts. Proton can also connect Gmail so new mail still arrives and you can send as the Gmail address while you migrate. That connection means Google is still in the loop until you disconnect it later.

            Also good: Tuta (encrypted, Germany), Fastmail (excellent paid mail, Australia). Self-host: a mail server or Nextcloud is “own everything” and more work.

            Always also keep a Takeout MBOX as your own archive (Thunderbird can open MBOX). Imports can take hours or days for a large inbox. Use up to the storage you actually have on Proton — check the quota.

            Update logins, bank mail, and 2FA recovery to the new address before you delete Gmail. Forwarding/connection is a bridge, not the destination.
            """),

        T("calendar", "Leave Google Calendar",
            ["calendar", "kalender", "ics", "caldav", "proton calendar", "tuta calendar"],
            """
            Easy: Proton Easy Switch imports Google calendars with mail. Or Takeout → .ics → import into Proton Calendar, Tuta Calendar, or Nextcloud (CalDAV).

            Then: add the new account on phone and PC, hide or remove the Google calendar account, and check that the next week of events is really there. Shared calendars with people still on Google may need a new sharing method (CalDAV, export, or they stay on Google).
            """),

        T("contacts", "Leave Google Contacts",
            ["contacts", "kontakter", "vcard", "vcf", "carddav"],
            """
            Easy Switch imports contacts with Gmail, or Takeout → .vcf (vCard) → import into Proton, Tuta, or Nextcloud CardDAV.

            On the phone, add the new account and set it as the default for new contacts before you remove the Google account. SIM/device contacts are separate from Google Contacts.
            """),

        T("drive", "Leave Google Drive",
            ["drive", "google drive", "files", "filer", "nextcloud", "proton drive", "sync.com"],
            """
            Takeout preserves folders. Then upload to Proton Drive (encrypted, Windows app) or Nextcloud / a host you pay. Sync.com is another zero-knowledge cloud.

            Do not delete Drive until file counts and a few known folders match. Shared Drive / Workspace files may belong to an organisation — you cannot take those with a personal Takeout.

            Drive for desktop on Windows: uninstall from This PC after files live elsewhere. Do not wipe the local Google folder by hand if you still need the cache; use the official uninstaller.
            """),

        T("docs", "Leave Google Docs and Sheets",
            ["docs", "sheets", "slides", "document", "spreadsheet", "libreoffice", "cryptpad", "onlyoffice", "proton docs"],
            """
            Takeout can give .docx / .xlsx copies. Open them in LibreOffice (offline, free), ONLYOFFICE, or Proton Docs/Sheets. Real-time collab without Google: CryptPad (browser, encrypted) or a self-hosted ONLYOFFICE/Nextcloud.

            Comments, suggesting mode, and Apps Script do not move 1:1. Export a PDF of anything you must keep as-laid-out. Tell collaborators the new link before you turn off the Google Doc.
            """),

        T("photos", "Leave Google Photos",
            ["photos", "photo", "billeder", "ente", "immich", "library"],
            """
            Takeout: deselect all, tick only Google Photos, ZIP format. Google often splits huge libraries into several zips.

            Easy: Ente desktop app → Upload → Google Takeout. Official Ente docs: unzip all zips into one parent folder, keep subfolders (do not flatten), then import that folder so JSON sidecars match files. Ente is end-to-end encrypted and tries to deduplicate exact copies (Takeout repeats photos across albums). Storage is based on unique originals, not zip size. Compare photo+video counts before you disconnect Google Photos.

            Own: Immich (self-host) plus immich-go for Takeout-style import. Smaller libraries can live on Proton Drive.

            Live/motion photos may arrive as separate image+video. Partner-shared Google photos are easy to miss — check Ente’s partner-shared option if you used that.
            """),

        T("chrome", "Leave Chrome",
            ["chrome", "browser", "brave", "firefox", "librewolf", "default browser"],
            """
            Order: install Firefox (stronger de-Google) or Brave (feels like Chrome; it is still Chromium). Import bookmarks, passwords, and (if you want) extensions. Set the new browser as Windows default (Settings → Apps → Default apps) before uninstalling Chrome.

            Brave is Chromium-based: similar websites, not “no Google on the internet.” Firefox/LibreWolf share less DNA with Chrome. LibreWolf is Firefox with tighter defaults.

            Then uninstall Chrome from This PC. This app launches the official uninstaller; it does not delete your Chrome profile folder. Sign out of the Google account in Chrome first if you keep the profile as a backup.
            """),

        T("search", "Leave Google Search",
            ["search", "søg", "duckduckgo", "kagi", "startpage", "brave search", "searx"],
            """
            No personal archive to move. In the new browser, set the default search engine.

            Brave Search and DuckDuckGo are the usual free picks. Kagi is paid and high quality. SearXNG is self-hosted metasearch. Startpage shows Google-style results without you being the logged-in Google searcher — it is a privacy proxy, not a Google-free index.

            Also turn off Web & App Activity in the Google account while you still have it (myactivity.google.com) — that is not the same as changing search.
            """),

        T("passwords", "Leave Google Password Manager",
            ["password", "passwords", "adgangskode", "bitwarden", "proton pass", "csv"],
            """
            Chrome: Settings → Passwords → Export. Takeout may also include Passwords.csv. Both are plaintext. Import into Proton Pass or Bitwarden, confirm a few logins, then delete the CSV and empty Recycle Bin.

            This app warns if Takeout contains that file. Never email the CSV or leave it on the Desktop.

            After import, turn off Google password save in Chrome (or uninstall Chrome) so new passwords do not go back to Google.
            """),

        T("auth", "Leave Google Authenticator",
            ["authenticator", "2fa", "totp", "aegis", "ente auth", "otp"],
            """
            Do this before a phone reset or GrapheneOS install.

            In Google Authenticator: menu → Transfer accounts → Export accounts → QR codes (Google splits many accounts across several QRs). In Ente Auth: Import → Google Authenticator. In Aegis: add → scan those QRs. Keep Google Authenticator until every important login (mail, bank, GitHub, Proton) produces a working code in the new app.

            Then make an encrypted Aegis backup or turn on Ente Auth backup. Store backup codes for each site somewhere that is not Google Drive.
            """),

        T("youtube", "YouTube has no honest 1:1 replacement",
            ["youtube", "yt ", "freetube", "newpipe", "invidious", "peertube", "odysee"],
            """
            There is no full replacement for YouTube’s catalog, comments, TV apps, and uploads.

            Reduce: FreeTube (Windows), NewPipe (Android, F-Droid), Invidious (web). They still talk to YouTube’s catalog — we label that. You avoid a Google login and the official app.

            Leave: watch on PeerTube, Odysee, Vimeo, or the creator’s own site where the video exists. Many channels are YouTube-only.

            Archive: Takeout includes subscriptions, playlists, history, and your uploads. This app can turn subscriptions CSV into OPML for FreeTube. Pick Reduce / Leave / Archive on Your plan. Do not pretend NewPipe “de-Googles the videos.”
            """),

        T("ytmusic", "Leave YouTube Music",
            ["youtube music", "ytmusic", "playlist", "tidal", "navidrome", "tunemymusic"],
            """
            Playlists/likes can be copied with TuneMyMusic (tunemymusic.com) to TIDAL, Spotify, Deezer, and others. That site is a third party — you use their wizard; this app does not store the library.

            Own: local files + Navidrome or similar. Takeout still has the YouTube Music / YouTube folder as an archive.
            """),

        T("maps", "Leave Google Maps saved places",
            ["maps", "kort", "organic maps", "osmand", "places", "gpx", "kml"],
            """
            Navigation: Organic Maps or OsmAnd (OpenStreetMap, offline, no Google account). HERE WeGo is another non-Google option.

            Saved places: Takeout Maps / Saved Places. This app converts them locally to GPX, KML, GeoJSON, and CSV. Organic Maps officially imports KML, KMZ, GPX, and GeoJSON (open the file with Organic Maps, or Bookmarks → Import). Not every Google list, review, or Live View feature exists on OSM.

            Timeline/Location History is separate and sensitive — keep it local, do not upload the raw file to random clouds.
            """),

        T("keep", "Leave Google Keep",
            ["keep", "notes", "noter", "joplin", "standard notes", "markdown"],
            """
            Takeout Keep is JSON. This app converts it locally to Markdown. Import that into Standard Notes or Joplin. Attachments/drawings may need a manual copy from the Takeout Keep folder. Check a few notes before you turn Keep off.
            """),

        T("meet", "Leave Meet, Chat, and Voice",
            ["meet", "hangouts", "chat", "voice", "jitsi", "signal", "element", "matrix"],
            """
            Meetings: Jitsi Meet (meet.jit.si) works in a browser with no account. Self-host Jitsi or use Element Call if you want it on your server.

            Chat: Signal (private messenger), Element/Matrix, or plain SMS. Meet/Chat history is not a clean 1:1 export into Signal. Google Voice numbers are a US-centric product — port the number out before you delete the account if you still need it.
            """),

        T("dns", "Leave Google Public DNS (8.8.8.8)",
            ["dns", "8.8.8.8", "8.8.4.4", "quad9", "9.9.9.9", "mullvad"],
            """
            8.8.8.8 / 8.8.4.4 (and IPv6 2001:4860:4860::8888 / ::8844) are Google Public DNS. The resolver sees the hostnames your PC looks up.

            Quad9 (Swiss non-profit): 9.9.9.9 and 149.112.112.112. They publish that they do not store client IPs to disk. The default Quad9 service also blocks many malware/phishing domains. Mullvad DNS is another no-logs option. Unbound on your router is the “own it” option.

            In this app, Network → Quad9 is Lifetime Pro, asks Windows for admin, saves a backup, and can restore. Changing DNS is not a VPN and does not hide your IP from websites.
            """),

        T("android", "De-Google the phone",
            ["android", "pixel", "graphene", "calyx", "f-droid", "aurora", "play services", "phone", "mobil"],
            """
            Strongest: GrapheneOS on an officially supported Pixel. As of GrapheneOS’s public FAQ (2026), production devices are Pixel 6 through Pixel 10 families (including many Pro / Fold / “a” models, Pixel Fold, Pixel Tablet). Always re-check grapheneos.org/faq before you buy — support lists change. GrapheneOS has publicly warned against Pixel 11 over missing MTE. Install from a separate computer; unlock bootloader; do not factory-reset until 2FA works in Ente Auth/Aegis and Takeout is done.

            GrapheneOS can run sandboxed Play services if you still need a few Play apps — that is a compromise, not “no Google.”

            Stay on stock Android: F-Droid + Aurora Store, disable or restrict Play, use a hardened browser. Weaker, but better than doing nothing.

            Other OS: CalyxOS, /e/OS, Lineage without GApps — different trade-offs. Not every phone can unlock.

            Back up SMS/call logs if you care; they are not in a typical Takeout the way Photos are.
            """),

        T("translate", "Leave Google Translate",
            ["translate", "oversæt", "deepl", "libretranslate"],
            """
            Firefox Translations can run on-device in Firefox. DeepL is often stronger for EU languages (commercial, not Google). LibreTranslate is open / self-host. None of these is a pixel-perfect Google Translate clone, and camera-translate is the hard part.
            """),

        T("home", "Google Home / Nest",
            ["nest", "home mini", "google home", "home assistant", "chromecast"],
            """
            Home Assistant can talk to many Nest/Home devices during a slow move. Some features still call Google’s cloud. Matter/Thread devices are easier to reuse. Speakers that are “just a Google Assistant” often have no clean local replacement besides replacing the hardware.

            Chromecast: use the TV’s own apps, an Apple TV, a computer, or a non-Google stick. Casting from Chrome specifically goes away when you leave Chrome.
            """),

        T("pay", "Google Pay / Wallet",
            ["pay", "wallet", "google pay", "mobilepay", "betal"],
            """
            Move cards back into the bank’s own app (contactless) or MobilePay in Denmark. Google Pay transaction history can appear in Takeout — keep it if you need records, then add the cards elsewhere before you remove the Google account from the phone. Virtual-card services are extra, not required.
            """),

        T("ads", "Cut Google profiling while the account still exists",
            ["ads", "adcenter", "activity", "my activity", "personalisation", "tracking"],
            """
            This is not leaving Google, but it shrinks profiling: myadcenter.google.com (ad personalisation off), myaccount.google.com/activitycontrols (Web & App Activity, Location, YouTube history), myactivity.google.com (delete saved activity). Auto-delete is available in the account. Do this in parallel with Takeout, not instead of moving data.
            """),

        T("gdpr", "Your GDPR rights vs Google",
            ["gdpr", "datatilsynet", "erasure", "art. 17", "art 17", "art. 15", "slet konto", "delete account", "privacy"],
            """
            In the EEA you can: access (Art. 15), portability (Art. 20 — Takeout is the practical tool), erasure (Art. 17), withdraw consent / object (Arts. 7 and 21), complain (Art. 77). Google must usually answer within one month (Art. 12(3)).

            This app’s Rights tab has copy-paste templates. Complaints in Denmark: Datatilsynet (datatilsynet.dk). Some legal/security logs they may keep.

            Delete account (myaccount.google.com/deleteaccount) is last. Workspace/school accounts follow the organisation’s admin, not this button.
            """),

        T("workspace", "Work or school Google (Workspace)",
            ["workspace", "school", "gsuite", "classroom", "admin", "company"],
            """
            If the account is owned by an employer or school, you often cannot delete it, and Takeout may be limited by admin policy. Copy what you are allowed to keep (your own files) using their export tools, then use a personal Proton/Ente life in parallel. Classroom, corporate Drive, and Meet stay Google if the institution says so. This app is for your personal footprint on this Windows PC.
            """),

        T("assistant", "Google Assistant and Gemini",
            ["assistant", "gemini", "bard", "ok google"],
            """
            There is no drop-in “OK Google” replacement that is both private and as integrated. Home Assistant Assist is the self-hosted direction. Gemini chats can appear in Takeout — treat them as sensitive.

            This app blocks Google Gemini as a coach provider on purpose.
            """),

        T("play", "Play Store without a Google account",
            ["play store", "play services", "f-droid", "aurora", "apps"],
            """
            F-Droid: free/open Android apps. Aurora Store: Play catalog without using the official Play app (still Google’s catalog underneath). GrapheneOS sandboxed Play is for the leftover apps you cannot replace. Paid Play purchases generally stay tied to Google.
            """),

        T("fit", "Google Fit / health",
            ["fit", "fitbit", "health", "sundhed"],
            """
            Takeout can include Fit / Fitbit-related data. There is no single EU-friendly 1:1. Keep the export. Phone health apps (e.g. the OEM’s, or Gadgetbridge for some wearables) are the next step. Do not upload raw health exports to random websites.
            """),

        T("leftovers", "Leftovers on this Windows PC",
            ["leftover", "uninstall", "this pc", "folders", "startup", "extension"],
            """
            This PC scan looks at installed programs, Google folders, scheduled tasks, Windows services, Chrome extensions that talk to Google, running processes, startup entries, signed-in Chrome accounts, and DNS.

            Uninstall uses the official uninstaller. Profiles under LocalAppData\Google are not auto-deleted (passwords and Drive caches live there). After uninstall, Scan again. DNS restore is in Network.
            """),

        T("never", "What this app will not do",
            ["never", "auto delete", "safe", "wreck", "risk"],
            """
            DeGoogle Kit does not: log into Google for you, scrape Gmail with your password, silently uninstall Chrome, wipe Drive/Chrome folders, or erase the Google account. You click each destructive step. Scan, plan, Takeout parse, catalog, GDPR templates, and this offline coach are free. Pro is extras (DNS apply, HTML report). Hosted DeGoogle AI (Cloud Pass) is coming later — until then use Offline or your own key.
            """),

        T("verify", "Verify before you disconnect",
            ["verify", "counts", "imported", "ready to disconnect", "sammenlign"],
            """
            Imported ≠ verified. Check: last week of mail, photo count ± a few (duplicates exist in Takeout), a known Drive folder, calendar tomorrow, a 2FA code, bookmarks in the new browser. Then mark verified in Your plan. Disconnecting Google after that is still a manual step on Google’s site.
            """)
    ];

    private static OfflineTopic T(string id, string title, string[] keys, string body) =>
        new()
        {
            Id = id,
            Title = title,
            Keys = keys,
            Body = body.Replace("\r\n", "\n").Trim()
        };
}
