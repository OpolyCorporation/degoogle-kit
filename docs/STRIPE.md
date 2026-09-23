# Stripe setup — DeGoogle Kit (account `degoogle`)

Live account: `acct_1U6djFFdVhE7n9Zo`  
Catalog created 2026-09-23. Keep `CatalogIsLive` / `CHECKOUT_IS_LIVE` **false** until license-api is on public HTTPS and a test purchase works.

## Live products (Payments)

| Plan | Product id | Price id | Amount |
|------|------------|----------|--------|
| Lifetime Pro | `prod_VJaX0tVMlmF7ai` | `price_1UIxMfFdVhE7n9ZovlWDpG1V` | €29.99 once |
| Household | `prod_VJaXDE0hVuCBkF` | `price_1UIxMkFdVhE7n9ZocPhNLWSF` | €59.99 once |

Metadata on both: `sku` = `lifetime` | `family`, `seats` = `1` | `3`.

## license-api env

```env
STRIPE_API_KEY=rk_live_…   # restricted key preferred; never put in the Windows app
STRIPE_PRICE_LIFETIME=price_1UIxMfFdVhE7n9ZovlWDpG1V
STRIPE_PRICE_FAMILY=price_1UIxMkFdVhE7n9ZocPhNLWSF
STRIPE_WEBHOOK_SECRET=whsec_…
LICENSE_SIGNING_KEY=…
SUPABASE_URL=https://pgkcsbyukqmafzwpsyfo.supabase.co
SUPABASE_ANON_KEY=…
SUPABASE_SERVICE_ROLE_KEY=…
```

Webhook endpoint (after host): `https://YOUR-LICENSE-API/webhook`  
Events: `checkout.session.completed`, `checkout.session.async_payment_succeeded`

Website: set `LICENSE_API_URL` / `DGK_LICENSE_API` to that HTTPS base so `/api/checkout` creates Sessions instead of Payment Links.

## Still later

- Cloud Pass Billing prices (`STRIPE_PRICE_CLOUD_*`) — not sold yet  
- Stripe Tax / VAT registration — threshold monitoring only for now  
- Add **test mode** account to Cursor Stripe MCP (Dashboard → manage accounts) for safe end-to-end tests  
- Flip live flags only after a full paid→key path works

## Integration shape (accepted planner)

Stripe-hosted Checkout on the web after the site’s 14-day withdrawal checkbox.
