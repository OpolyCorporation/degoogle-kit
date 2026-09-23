# DeGoogle Kit — agent briefing

Windows desktop app that helps people **leave Google without wrecking their PC**. It never auto-deletes Google data or accounts. Users choose every uninstall, DNS change, and disconnect step.

This repo is the **app**. The marketing/download site is a separate Lovable repo: `OpolyCorporation/degoogle` (local clone often at `C:\Users\jackj\Projects\degoogle`). Keep product facts aligned across both.

## Product in one sentence

Scan this Windows PC → pick replacements → parse Google Takeout locally → verify → disconnect (manual). Offline coach + optional BYOK AI help along the way.

## Repos and roles

| Repo | Role |
|------|------|
| `OpolyCorporation/degoogle-kit` (this) | WPF app (`net10.0-windows`), license-api, GitHub Pages legal HTML under `docs/` |
| `OpolyCorporation/degoogle` | Public website (TanStack Start + Lovable): download CTA, FAQ, install, ask-assistant, **shared free account** at `/account` |

**Public download URL (do not invent another):**  
`https://github.com/OpolyCorporation/degoogle-kit/releases/latest/download/DeGoogleKit-win-x64.zip`

**Website account (same Supabase as this app):**  
Preview: `https://id-preview--d11fe691-44fe-4cc4-84a5-135f3b0e7341.lovable.app/account`  
In-app: `LegalCopy.WebsiteAccountUrl` — update `LegalCopy.WebsiteUrl` when a custom domain is live.

**Legal pages (also mirrored in-app as `LegalCopy`):**  
`https://opolycorporation.github.io/degoogle-kit/privacy.html`  
`https://opolycorporation.github.io/degoogle-kit/terms.html`

## Shared free account

One email/password account works in **both** the Windows app and the website:

- Supabase Auth + RLS tables `progress`, `licenses`, `keepalive` (see `supabase/schema.sql` / `cloud/supabase.json`)
- App: Pro tab → Sign in / Create account; also **Open account on website**
- Website: `/account` — sign up/in, view synced checklist/plan summary and linked license
- Never put the **service role** key in the app or website; only the public anon key
- Product positioning: **app is the sell**; website is download + account + future light dashboard

## Stack (app)

- C# / WPF, single-file self-contained publish to `artifacts/win-x64`
- Licensing: `Licensing/` (ECDSA tickets `DGK2.…`)
- Backend helper: `license-api/` (Stripe session → key, optional hosted coach `/v1/coach`)
- Optional account: Supabase project **degoogle** (`pgkcsbyukqmafzwpsyfo`, same as the website `/account`). Config: `cloud/supabase.json` anon only in app
- Secrets: Windows DPAPI via `SecretStore` — never commit `.env`, service role, or signing private keys

## Brand

- Navy `#071422`, toolbox blue `#2F7BFF`, mint `#3EE6A8`
- Product name is hero-level on promotional surfaces; do not invent a purple/cream AI-slop look

## Trader / legal

- **Opolyonix Corp**, CVR **43410369**, Esbjerg Ø, Denmark
- Privacy/terms notice version is `LegalCopy.NoticeVersion` (currently **v7**) — bump both in-app `LegalCopy` and `docs/privacy.html` / `docs/terms.html` together
- Website `/privacy` and `/terms` must say the same: shared account DB, same export/delete as the app, checkout 14-day checkbox
- EU 14-day withdrawal for unused digital keys; Pro trial is separate (7 days, no card)

## Pricing (honest status)

| Plan | Price | Status |
|------|-------|--------|
| Free | €0 | Scan, plan, Takeout parse, catalog, GDPR templates, offline coach |
| Lifetime Pro | €29.99 once | DNS apply + HTML report extras; Stripe Checkout |
| Household (Stripe sku `family`) | €59.99, up to 3 PCs | Same Pro features — multi-PC seats for one home. Website: owner invites by email; invitees get one-time login codes until they claim a PC code (then unlock activation). Unclaimed invitees stay data-minimized. Skip if only one machine. |
| Pro trial | 7 days | Requires signed-in free account; cloud tracks `trial_started_at` |
| Cloud Pass | €4.99/mo or €39/yr | Hosted DeGoogle AI — **code exists, not billed** until live Stripe catalog |

- `StripeStore.CatalogIsLive = false` — Checkout URLs are still **test** Payment Links. Buy buttons may open test Checkout after an explicit confirm so the Household flow can be walked.
- In-app label is **Household**; Stripe/legal may still say Family.
- Do **not** enable Stripe Tax without VAT registration
- Do **not** sell or market Cloud Pass as live until license-api is public HTTPS **and** has `GROQ_API_KEY`

## AI coach (important)

### What works today

1. **Offline / Topics** — local fact-checked guides in `Services/OfflineGuide.cs`. No network.
2. **Send (BYOK)** — user pastes a **Groq** key (`gsk_…`). App calls Groq Chat Completions. Default model: **`openai/gpt-oss-120b`**. Old Llama ids on Groq **404** — always normalize via `AiProviders.NormalizeModel`.
3. Coach is a **de-Google agent/chatbot**, not everyday ChatGPT. Prompt steers off-topic users back.

### What is half-built

- **DeGoogle AI** button → `POST {LicenseApiUrl}/v1/coach` with Cloud Pass key
- Server uses **our** Groq key from `license-api/.env` (`GROQ_API_KEY`)
- Default `LicenseApiUrl` is `http://127.0.0.1:5288` — not usable for public users until hosted
- Gemini / Google AI providers are **blocked** on purpose

### Honesty rules in copy and AI

- No 1:1 YouTube replacement (FreeTube/NewPipe still use YouTube’s catalog)
- Brave is still Chromium
- Easy Switch Gmail connection keeps Google until the user disconnects it
- App never auto-deletes Google
- Do **not** tell users they can farm trials by creating new accounts

## Architecture map (app)

```
MainWindow.*          UI tabs: This PC, Plan, Coach, Rights, Network, Takeout, Pro, Privacy
Services/GoogleScanner.cs   PC scan
Services/Takeout*.cs        Local Takeout parse/convert
Services/OfflineGuide.cs    Offline coach corpus
Services/AiCoach.cs         BYOK + hosted client
Services/AiProviders.cs     Provider catalog (Groq first)
Services/LicenseService.cs  Pro / trial / Cloud Pass local state
Services/StripeStore.cs     Payment links + license API URL
Services/AccountService.cs  Optional Supabase account
Services/LegalCopy.cs       Privacy/terms text (single source for notice version)
license-api/                Stripe redeem + /v1/coach
docs/                       GitHub Pages privacy/terms/landing
updates/latest.json         In-app update feed (must match GitHub release zip)
updates/whats-new.txt       Short bullets shown in the update dialog — edit before each tag
```

## Release reality (as of 2026-09)

- Public Windows zip target: **v0.4.1** (coach/Household/Norton packaging) — confirm `updates/latest.json` and GitHub Releases after CI finishes
- Before tagging: rewrite `updates/whats-new.txt` with short “What’s new” bullets (CI copies them into `latest.json` changelog)
- App `Version` in `DeGoogleKit.csproj` must bump when cutting a new zip
- Builds are **not code-signed** yet unless `WINDOWS_CERT_PFX` + `WINDOWS_CERT_PASSWORD` secrets are set — SmartScreen/Norton reputation warnings are expected until then
- To sign: buy an **OV** Authenticode cert for Opolyonix Corp (CVR 43410369), export PFX, base64-encode into GitHub secret `WINDOWS_CERT_PFX`, password in `WINDOWS_CERT_PASSWORD`, retag a release. EV is optional later if Norton stays noisy.
- Public download stays `releases/latest/download/DeGoogleKit-win-x64.zip` (website `DOWNLOAD_URL` + in-app feed)
- Website checkout: `/checkout` with 14-day checkbox → `/api/checkout` → license-api `POST /v1/checkout` when `LICENSE_API_URL` is set, else Stripe Payment Link fallback
- Live Stripe catalog: see `docs/STRIPE.md` (Lifetime + Household price ids). Do **not** flip live flags until license-api is public HTTPS
- App Export/Delete call Supabase `export_my_data` / `delete_my_account` (same as website MyDataPanel)
- Releases ship as a **self-contained folder** (not `PublishSingleFile`) so fewer AVs treat the binary like a temp dropper
- Kill `DeGoogleKit.exe` before `dotnet publish` if the file is locked

## Hard constraints for agents

1. Never commit `.env`, service-role keys, Stripe secret keys, or license signing private keys
2. Never force-push `main` / rewrite published history without explicit user request
3. Only commit/push when the user asks
4. Do not implement or sell hosted DeGoogle AI as live without public license-api + Groq key
5. Keep in-app legal copy and `docs/*.html` in sync when changing notices
6. Prefer fixing/hardening over inventing exploit/PoC paths
7. Kill `DeGoogleKit.exe` before `dotnet publish` if the file is locked

## Useful local paths

- Installed app (after Setup / publish): `%LOCALAPPDATA%\Programs\DeGoogleKit\DeGoogleKit.exe`
- Publish output: `artifacts/win-x64/DeGoogleKit.exe`
- User data: `%LOCALAPPDATA%\DeGoogleKit\` (plan, license, settings, DPAPI key)

## When changing the website too

Open `../degoogle` (or `C:\Users\jackj\Projects\degoogle`). Product accounts must stay on Supabase **degoogle** (`cloud/supabase.json`) — the website uses `VITE_DGK_SUPABASE_*` / `src/lib/config.ts` for the same project. Lovable Cloud on the site is admin-only. Download button must keep pointing at the **latest release zip** URL above. Do not contradict Free vs Pro vs Cloud Pass status. Lovable repo: never force-push / rebase published history (see that repo’s `AGENTS.md`).
