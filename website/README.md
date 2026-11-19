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

## App screens

The gallery in `AppShowcase.tsx` shows six screens rendered by `npm run screens`
(`scripts/generate-app-screens.mjs`) into `public/app-*.jpg`: the network picker,
a network's fuel list, the package ladder, the wallet, the map and the savings
screen.

They used to be device captures, and they aged into a lie — purple accents that
turned green, an unstyled map canvas printing "API KEY REQUIRED" over Kyiv, a
basket whose price did not multiply out. A visitor reads a stale capture as the
product, so the gallery is now generated from what the app ships:

- colours come from the `obsidian` theme in `mobile/src/core/design/themes.ts` —
  the violet the screenshots show, not the `lemberg` green of the default;
- the backdrop is `GridBackground` (a 40px grid at ~3% primary plus two radial
  glows);
- numbers are Rajdhani and Cyrillic is not, because Rajdhani has no Cyrillic
  glyphs: on a device every Ukrainian word falls back to the system face and the
  digits keep the display face. The renders reproduce that split with a 14 KB
  subset in `scripts/assets/` (SIL OFL, see the notice there).

These are marketing renders, not device captures. Everything personal is
invented — the station, the phone tail, the voucher id — and the QR is the demo
token from `src/config/qr.json`, which is non-routable on purpose. Only reviewed,
non-sensitive images may be placed in `public/`: never a capture containing
customer names, phone numbers, email addresses, voucher ids or a live QR payload.

`AppShowcase.tsx` owns the manifest and `scripts/check-site-contract.mjs` pins the
set, so a screenshot added to `public/` without a manifest entry — or a sensitive
file that reappears — fails `npm run check`.

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

The lockup is the one brand asset that is not generated: `public/fuelflow-logo.webp` is the prototype's own drawing - the lime "F" bolt and the words in one file - keyed out of its near-black plate so it is white-and-lime alpha, the same recipe as the lion mask. `BrandLockup.tsx` is the single place that paints it, once, in the header and the footer, never again over the hero photograph. The lion is the product's mark and stays in the tab icon, the admin and the launcher art; it is deliberately not part of the lockup.

## Checks

`npm run check` runs against the built export:

- `check:header` — responsive navigation ladder and phone menu reachability
- `check:hero` — settled/no-JavaScript hero visibility, CTA fit, overflow, one lockup in the header and a baked-copy/ baked-accent pixel scan of the photograph
- `check:headers` — real nginx caching and 404 behavior
- `check:brand` — byte/reference parity for every lion asset
- `check:taps` — 44px mobile controls or the WCAG spacing exception on all routes
- `check:lead` — client validation and 202/400/429/5xx/offline B2B-form states plus payload shape
- `check:site` — reviewed screenshot allowlist, legacy anchors and business-lead legal disclosures
