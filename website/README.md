# FuelFlow marketing site

Static Next.js 16 App Router export for `fuelflowmarketplace.com`.

## Stack

- Next.js 16, React 19, TypeScript, static `output: 'export'`
- CSS Modules with editorial design tokens in `src/app/globals.css`
- Self-hosted Inter and Inter Tight Cyrillic/Latin subsets
- Functional CSS motion only: hero entrance, navigation, gallery and disclosure states; reduced motion is respected

## Develop

```bash
npm ci
npm run dev
npm run lint
```

`npm run dev` regenerates the App Store QR and shared brand assets first. To preview only the site without touching generated assets in sibling packages, run `npx next dev` directly.

## Production

```bash
npm run build
npm run check
```

The static export is written to `out/` and served by `website/nginx.conf` in production.

## Routes and composition

Public routes are `/`, `/support/`, `/privacy/` and `/terms/`.

The homepage is composed in `src/app/page.tsx` from:

1. B2B-first hero
2. services block - what FuelFlow does for a company, plus the business-document roadmap
3. company fuel-pool operating model
4. application screenshot rail
5. live catalogue and partner-network ledger
6. persisted business-lead form
7. five-step consumer flow
8. FAQ
9. app/contact closing field

The catalogue comes from `/api/packages`; `src/lib/catalogue.ts` filters test and non-traded fuels and shares one request between every display. The B2B form posts to `/api/business-leads`; support remains a separate `/api/support/messages` flow.

## What the site claims

**FuelFlow sells a service, not fuel** (planning repo `docs/SALES_MODEL.md`: "What the customer buys - the sale is an advertising service"). So no copy on this site may say the customer bought or paid for fuel: the app may name the nominal ("10 л А-95", "код на пально"), and the page may say a package, a code and a price - but the purchase is always the service.

The service block and the business-document panel in `Services.tsx` carry a status chip per item, and the chip is a claim about the app, not about the marketing:

- `працює` - the shipped code does this today (contracts and legal entities, the company pool, issuing/recalling/freezing a worker's code, the live catalogue).
- `у планах` - the roadmap says it will (the business documents: acts of reconciliation, advance invoices, tax invoices, VAT closing; cross-border provider contracts; transfers between members).

Before flipping a chip, prove the feature in the app, and record the evidence the way the planning repo does. A chip that lies is worse than a chip that is quiet.

## App screenshot assets

`AppShowcase.tsx` owns an explicit curated manifest. Only reviewed, non-sensitive screenshots may be placed in `public/` and referenced there. Never publish captures containing customer names, phone numbers, email addresses, voucher ids or live QR material.

## Hero photograph

`public/hero-prototype-clean.webp` is the approved landing-page prototype's forecourt, cropped to its hero band with the prototype's own copy removed: its nav row, headline, lead, both CTAs and the "Download App" pill were masked and inpainted out of the pixels. The page paints all of that from HTML in Ukrainian, and the header owns the brand lockup - so the hero itself carries exactly one image.

The photograph was not regenerated, only healed, and the repair lives in the asset. `check:hero` measures it: a run of text-like rows or of lime rows in the left band means a second, already-finished advert is sitting under the live copy. The phone on the right keeps the prototype's English screen; it is product imagery, and the narrow-width crops are anchored to keep the canopy sign and the car rather than that screen.

## Brand assets

Every lion is derived from `../mobile/assets/icon.png`:

```bash
npm run brand
npm run check:brand
```

`scripts/generate-brand-assets.mjs` writes the website/admin tab icons, UI masks, Apple icon and Android launcher foreground. The App Store QR is generated from the canonical URL with `npm run qr`.

The wordmark is the one brand asset that is not generated: `public/fuelflow-wordmark.webp` is the prototype's own type, keyed out of its near-black plate so it is white alpha, the same recipe as the lion mask. `BrandLockup.tsx` is the single place that pairs it with the lion - the prototype's green "F" bolt is not shipped, because the lion stands in its place, and the lockup is painted once, in the header, never again over the hero photograph.

## Checks

`npm run check` runs against the built export:

- `check:header` — responsive navigation ladder and phone menu reachability
- `check:hero` — settled/no-JavaScript hero visibility, CTA fit, overflow, one lockup in the header and a baked-copy/ baked-accent pixel scan of the photograph
- `check:headers` — real nginx caching and 404 behavior
- `check:brand` — byte/reference parity for every lion asset
- `check:taps` — 44px mobile controls or the WCAG spacing exception on all routes
- `check:lead` — client validation and 202/400/429/5xx/offline B2B-form states plus payload shape
- `check:site` — reviewed screenshot allowlist, legacy anchors and business-lead legal disclosures
