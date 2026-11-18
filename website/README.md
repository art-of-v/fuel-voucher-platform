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
2. company fuel-pool operating model
3. application screenshot rail
4. live catalogue and partner-network ledger
5. persisted business-lead form
6. five-step consumer flow
7. FAQ
8. app/contact closing field

The catalogue comes from `/api/packages`; `src/lib/catalogue.ts` filters test and non-traded fuels and shares one request between every display. The B2B form posts to `/api/business-leads`; support remains a separate `/api/support/messages` flow.

## App screenshot assets

`AppShowcase.tsx` owns an explicit curated manifest. Only reviewed, non-sensitive screenshots may be placed in `public/` and referenced there. Never publish captures containing customer names, phone numbers, email addresses, voucher ids or live QR material.

## Brand assets

Every lion is derived from `../mobile/assets/icon.png`:

```bash
npm run brand
npm run check:brand
```

`scripts/generate-brand-assets.mjs` writes the website/admin tab icons, UI masks, Apple icon and Android launcher foreground. The App Store QR is generated from the canonical URL with `npm run qr`.

## Checks

`npm run check` runs against the built export:

- `check:header` — responsive navigation ladder and phone menu reachability
- `check:hero` — settled/no-JavaScript hero visibility, CTA fit, overflow and baked-copy pixel scan
- `check:headers` — real nginx caching and 404 behavior
- `check:brand` — byte/reference parity for every lion asset
- `check:taps` — 44px mobile controls or the WCAG spacing exception on all routes
- `check:lead` — client validation and 202/400/429/5xx/offline B2B-form states plus payload shape
- `check:site` — reviewed screenshot allowlist, legacy anchors and business-lead legal disclosures
