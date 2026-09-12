using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class GuideCatalog
{
    public static List<GuideItem> Create() =>
    [
        Item("browser", "Browser", "Replace Chrome", "Google Chrome",
            "Chrome ties search, sync, and your Google account together. A different browser is the highest-impact switch on a PC.",
            Alt("Firefox", "https://www.mozilla.org/firefox/", "Independent, extensions, good Windows support"),
            Alt("Brave", "https://brave.com/", "Chromium-based but no Google account required"),
            Alt("LibreWolf", "https://librewolf.net/", "Firefox with tighter privacy defaults")),

        Item("search", "Search", "Change your search engine", "Google Search",
            "Search queries are a detailed map of your life. Set the new browser’s default engine away from Google.",
            Alt("DuckDuckGo", "https://duckduckgo.com/", "Simple, private-by-default search"),
            Alt("Startpage", "https://www.startpage.com/", "Google results without Google tracking you"),
            Alt("Kagi", "https://kagi.com/", "Paid, high-quality, no ads")),

        Item("email", "Email", "Move off Gmail", "Gmail",
            "Export mail, then forward or switch sending address. Do this before you delete the Google account.",
            Alt("Proton Mail", "https://proton.me/mail", "Encrypted, Switzerland, free tier"),
            Alt("Tuta", "https://tuta.com/", "Encrypted mail and calendar"),
            Alt("Fastmail", "https://www.fastmail.com/", "Excellent paid mail, Australia")),

        Item("drive", "Files", "Replace Google Drive / Docs files", "Google Drive",
            "Download a Takeout of Drive first. Then pick a host you control or a privacy-respecting one.",
            Alt("Proton Drive", "https://proton.me/drive", "Encrypted cloud, Windows app"),
            Alt("Nextcloud", "https://nextcloud.com/", "Self-host or a trusted provider"),
            Alt("Sync.com", "https://www.sync.com/", "Zero-knowledge cloud")),

        Item("photos", "Photos", "Leave Google Photos", "Google Photos",
            "Takeout your library, then use something that does not scan photos for ads.",
            Alt("Ente", "https://ente.io/", "End-to-end encrypted photos"),
            Alt("Immich", "https://immich.app/", "Self-hosted Google Photos-style app"),
            Alt("Proton Drive", "https://proton.me/drive", "Fine for smaller libraries")),

        Item("youtube", "Video", "Watch YouTube without a Google account", "YouTube",
            "You can keep watching; you do not need a Google login or the official app.",
            Alt("FreeTube", "https://freetubeapp.io/", "Desktop YouTube client, no account"),
            Alt("NewPipe", "https://newpipe.net/", "Android, F-Droid"),
            Alt("Invidious", "https://invidious.io/", "Web frontend, no Google login")),

        Item("maps", "Maps", "Replace Google Maps", "Google Maps",
            "Offline-capable maps are often better for hiking and travel anyway.",
            Alt("Organic Maps", "https://organicmaps.app/", "Offline, no trackers"),
            Alt("OsmAnd", "https://osmand.net/", "Power-user offline maps"),
            Alt("Apple Maps / Here WeGo", "https://wego.here.com/", "Fine if you are not all-in on Google")),

        Item("calendar", "Calendar", "Move Calendar and Contacts", "Google Calendar",
            "Export .ics and vCard from Google, then import. Update phone accounts after email.",
            Alt("Proton Calendar", "https://proton.me/calendar", "Works with Proton Mail"),
            Alt("Tuta Calendar", "https://tuta.com/", "Bundled with Tuta mail"),
            Alt("Nextcloud", "https://nextcloud.com/", "CalDAV + CardDAV")),

        Item("docs", "Documents", "Stop using Google Docs / Sheets", "Google Docs",
            "Download copies as .docx / .xlsx / .odt. Real-time collab is the hard part; CryptPad covers a lot of it.",
            Alt("LibreOffice", "https://www.libreoffice.org/", "Offline office suite"),
            Alt("CryptPad", "https://cryptpad.fr/", "Encrypted collaboration in the browser"),
            Alt("OnlyOffice", "https://www.onlyoffice.com/", "Docs with optional self-host")),

        Item("passwords", "Passwords & 2FA", "Leave Google Password Manager / Authenticator", "Google Password Manager",
            "Export passwords, move 2FA codes before you lose the phone or delete the account.",
            Alt("Bitwarden", "https://bitwarden.com/", "Open source password manager"),
            Alt("Proton Pass", "https://proton.me/pass", "If you already use Proton"),
            Alt("Aegis / Ente Auth", "https://ente.io/auth/", "Offline 2FA apps")),

        Item("meet", "Chat & Meet", "Replace Meet / Chat / Voice", "Google Meet",
            "For friends and small groups you do not need Google’s stack.",
            Alt("Signal", "https://signal.org/", "Private messenger"),
            Alt("Jitsi Meet", "https://meet.jit.si/", "Browser video calls, no account"),
            Alt("Element", "https://element.io/", "Matrix chat")),

        Item("translate", "Translate", "Replace Google Translate", "Google Translate",
            "Good enough for everyday use, and it does not feed Google your text.",
            Alt("DeepL", "https://www.deepl.com/", "Often better translations"),
            Alt("Firefox Translations", "https://www.mozilla.org/firefox/", "On-device in Firefox"),
            Alt("LibreTranslate", "https://libretranslate.com/", "Open source / self-host")),

        Item("android", "Phone", "De-Google the phone (optional)", "Google Play Services",
            "Biggest step. GrapheneOS on a Pixel is the gold standard. Aurora Store / F-Droid if you stay on stock Android.",
            Alt("GrapheneOS", "https://grapheneos.org/", "Pixel phones, hardened Android"),
            Alt("CalyxOS", "https://calyxos.org/", "Easier privacy-focused Android"),
            Alt("F-Droid + Aurora", "https://f-droid.org/", "Apps without a Google account")),

        Item("dns", "Network", "Stop using Google DNS", "8.8.8.8 / 8.8.4.4",
            "Google Public DNS still tells Google which sites you resolve. Quad9 and Mullvad DNS are drop-in replacements.",
            Alt("Quad9", "https://quad9.net/", "Malware blocking, privacy-focused"),
            Alt("Mullvad DNS", "https://mullvad.net/en/help/dns-over-https-and-dns-over-tls", "No-logs DNS"),
            Alt("Control D", "https://controld.com/", "Customizable filtered DNS")),

        Item("ads", "Ads", "Turn off Google ad personalisation", "Google Ads",
            "This does not leave Google, but it cuts a lot of profiling while you still have the account.",
            Alt("My Ad Center", "https://myadcenter.google.com/", "Turn personalisation off"),
            Alt("Activity controls", "https://myaccount.google.com/activitycontrols", "Web & App Activity, Location, YouTube"),
            Alt("Your data in Search", "https://myactivity.google.com/", "Delete what they saved")),

        Item("pay", "Payments", "Replace Google Pay / Wallet", "Google Pay",
            "Move cards back to the bank app or privacy-friendlier wallets before you remove the Google account from the phone.",
            Alt("Bank app", "https://www.nets.eu/", "Contactless from your Danish/EU bank"),
            Alt("Privacy.com / virtual cards", "https://privacy.com/", "US-oriented; EU users: bank-issued virtual cards"),
            Alt("Cash / MobilePay", "https://www.mobilepay.dk/", "Local option — still a company, not Google")),

        Item("takeout", "Account", "Download your data, then shrink the account", "Google Account",
            "Do Takeout before you uninstall anything important. After mail/photos/2FA are moved, delete unused Google services or the whole account.",
            Alt("Google Takeout", "https://takeout.google.com/", "Official export — use this first"),
            Alt("My Account", "https://myaccount.google.com/", "Turn off ads, 2FA backup, then delete"),
            Alt("Delete account", "https://myaccount.google.com/deleteaccount", "Last step, not the first"))
    ];

    private static GuideItem Item(string id, string category, string title, string google, string why, params Alternative[] alts) =>
        new()
        {
            Id = id,
            Category = category,
            Title = title,
            GoogleProduct = google,
            Why = why,
            Alternatives = alts
        };

    private static Alternative Alt(string name, string url, string note) =>
        new() { Name = name, Url = url, Note = note };
}
