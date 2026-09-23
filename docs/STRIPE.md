# Stripe setup — DeGoogle Kit (account `degoogle`)

Live account: `acct_1U6djFFdVhE7n9Zo`

**License minting runs on the marketing website** (Lovable / TanStack Start), not a separate host.
Paths: `/api/checkout`, `/api/license`, `/api/account/bind`, `/webhook`  
App-compatible aliases: `/v1/checkout`, `/v1/license`, `/v1/account/bind`

Keep `CatalogIsLive` / `CHECKOUT_IS_LIVE` **false** until a paid→DGK2 key path is verified with secrets set.

## Live products (Payments)

| Plan | Product id | Price id | Amount |
|------|------------|----------|--------|
| Lifetime Pro | `prod_VJaX0tVMlmF7ai` | `price_1UIxMfFdVhE7n9ZovlWDpG1V` | €29.99 once |
| Household | `prod_VJaXDE0hVuCBkF` | `price_1UIxMkFdVhE7n9ZocPhNLWSF` | €59.99 once |

## Lovable / website secrets (server-only)

```env
STRIPE_API_KEY=rk_test_…or_rk_live_…
LICENSE_SIGNING_KEY=base64-PKCS8-matching-app-PublicKeyPem
STRIPE_WEBHOOK_SECRET=whsec_…
STRIPE_PRICE_LIFETIME=price_1UIxMfFdVhE7n9ZovlWDpG1V
STRIPE_PRICE_FAMILY=price_1UIxMkFdVhE7n9ZocPhNLWSF
SUPABASE_SERVICE_ROLE_KEY=…   # for /v1/account/bind
```

Stripe webhook URL: `https://YOUR-SITE/webhook`  
Events: `checkout.session.completed`, `checkout.session.async_payment_succeeded`

Without `STRIPE_API_KEY`, `/checkout` falls back to test Payment Links.

## Windows app

`StripeStore.LicenseApiUrl` defaults to `LegalCopy.WebsiteUrl` so activate/bind hit the site.

## Still later

- Cloud Pass Billing prices  
- Stripe Tax when VAT-registered  
- Flip live flags after an end-to-end test  
- Optional: add Stripe **test mode** to Cursor MCP for safer dry-runs
