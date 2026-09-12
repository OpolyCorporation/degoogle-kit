using DeGoogleKit.Models;

namespace DeGoogleKit.Services;

public static class GdprCatalog
{
    public static IReadOnlyList<GdprRight> Rights { get; } =
    [
        new()
        {
            Article = "Art. 15",
            Title = "Access",
            WhatItMeans = "You can ask what personal data Google holds and how it is used.",
            HowToUse = "Google Account → Data & privacy, plus Takeout. Keep the download; that is your copy.",
            Url = "https://myaccount.google.com/data-and-privacy"
        },
        new()
        {
            Article = "Art. 20",
            Title = "Portability",
            WhatItMeans = "You can take your data to another service in a machine-readable form.",
            HowToUse = "Google Takeout is the practical tool. Do this before you delete the account.",
            Url = "https://takeout.google.com/"
        },
        new()
        {
            Article = "Art. 17",
            Title = "Erasure",
            WhatItMeans = "You can ask Google to delete data that is no longer needed, or if you withdrew consent.",
            HowToUse = "Delete individual products first, then the account. Some legal records they may keep.",
            Url = "https://myaccount.google.com/deleteaccount"
        },
        new()
        {
            Article = "Art. 7 & 21",
            Title = "Withdraw consent / object",
            WhatItMeans = "Turn off ad personalisation, Web & App Activity, Location History, and YouTube history.",
            HowToUse = "Data & privacy → History settings and Ad settings. This is not the same as deleting data.",
            Url = "https://myadcenter.google.com/"
        },
        new()
        {
            Article = "Art. 16 / 18",
            Title = "Rectify or restrict",
            WhatItMeans = "Fix wrong data, or ask Google to stop processing it while a dispute is ongoing.",
            HowToUse = "Use account info tools, then Google’s privacy contact if that fails.",
            Url = "https://support.google.com/policies/contact/general_privacy_form"
        },
        new()
        {
            Article = "Art. 77",
            Title = "Complain to Datatilsynet",
            WhatItMeans = "If you live in Denmark/EU, you can complain to your data protection authority.",
            HowToUse = "Datatilsynet handles GDPR complaints in Denmark. You can also contact Google’s EU DPO via their privacy form.",
            Url = "https://www.datatilsynet.dk/"
        }
    ];

    public static string AccessRequestTemplate =>
        """
        Subject: GDPR Article 15 / 20 request — Google Account

        Hello Google Privacy team,

        I am exercising my rights under the GDPR (Articles 15 and 20) as a data subject in the EEA.

        Please provide:
        1. Confirmation of whether you process my personal data
        2. Categories of data, purposes, recipients, and retention
        3. A copy of my data in a structured, commonly used, machine-readable format (Takeout is acceptable if complete)

        Account email: [YOUR EMAIL]
        Google account ID / recovery: [IF YOU HAVE IT]

        Please respond within one month as required by Article 12(3).

        Kind regards,
        [YOUR NAME]
        """;

    public static string ErasureTemplate =>
        """
        Subject: GDPR Article 17 request — erase my Google Account data

        Hello Google Privacy team,

        I request erasure of my personal data under Article 17 GDPR, after I have completed a Takeout export for my own records.

        Account email: [YOUR EMAIL]
        I have withdrawn consent for processing that was based on consent, and I object to processing based on legitimate interests where applicable.

        Please confirm what you deleted and what you retain (with the legal basis and period) within one month.

        Kind regards,
        [YOUR NAME]
        """;
}
