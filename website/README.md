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

**FuelFlow sells a service, not fuel** (planning repo `docs/SALES_MODEL.md`: "What the customer buys - the sale is an advertising service"). The substance is a set of tools for three audiences with the same problem - fuel suppliers, transport companies and drivers - and the company is nobody's competitor: it is the layer between the network and the person filling up. So no copy on this site may say the customer bought or paid for fuel: the app may name the nominal ("10 л А-95", "код на пальне"), and the page may say a package, a code and a price - but the purchase is always the service.

Two words carry a rule each, and `npm run check:copy` (`scripts/check-copy.mjs`) enforces both, because prose alone did not: **«пальне»** in every case, never «пально», and never **«пул»** - the business says закуплення, запас компанії or залишок. The check reads `src/`, so it covers headings, body copy, metadata and this repository's own notes.

The service block and the business-document panel in `Services.tsx` carry a status chip per item, and the chip is a claim about the app, not about the marketing:

- `працює` - the shipped code does this today (contracts and legal entities, the company pool, issuing/recalling/freezing a worker's code, the live catalogue).
- `у планах` - the roadmap says it will (the business documents: acts of reconciliation, advance invoices, tax invoices, VAT closing; cross-border provider contracts; transfers between members).

Before flipping a chip, prove the feature in the app, and record the evidence the way the planning repo does. A chip that lies is worse than a chip that is quiet.

## App screens

The gallery in `AppShowcase.tsx` shows six screens rendered by `npm run screens`
into `public/app-*.jpg`: the network picker, a network's fuel list, the package
ladder, the wallet, the map and the savings screen. What each screen says lives
in `scripts/lib/app-screens.mjs`; `scripts/generate-app-screens.mjs` only drives
the browser and writes the files.

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

The rendered JPEGs are **committed**, like `public/app-store-qr.svg`: the container
builds on `node:20-alpine`, which has no browser, so `npm run screens` falls back
to the committed files there and says so. `npm run check` is where a missing
browser is a hard failure, because a gate that cannot run must not pass silently.

## Hero photograph

`public/hero-prototype-clean.webp` is the approved landing-page prototype's forecourt, cropped to its hero band with the prototype's own copy removed: its nav row, headline, lead, both CTAs and the "Download App" pill were masked and inpainted out of the pixels. The page paints all of that from HTML in Ukrainian, and the header owns the brand lockup - so the hero itself carries exactly one image.

The photograph was not regenerated, only healed, and the repair lives in the asset. `check:hero` measures it: a run of text-like rows or of accent-coloured rows in the left band means a second, already-finished advert is sitting under the live copy. The accent test recognises both hues the file has carried - the lime it was drawn for and the violet the palette uses now - because a guard that only knows the old accent stops guarding on the day the accent changes.

**The photograph's accent is recoloured with the palette.** Its canopy edge light, the mock phone's own chrome and the green lights in `app-economy`, `app-networks`, `driver-phone` and `fuel-nozzle` were shifted from the lime hue to the violet one (a hue rotation in HSV over the saturated lime pixels only; nothing was repainted by hand and no pixel without lime in it was touched). Without it the page is purple around a green-lit forecourt, which reads as two brands. If the palette ever goes back to lime, those files go back too - they are the same images, and the shift is a one-way edit with no script in the repository, so the originals come from the prototype rather than from a re-run. The phone on the right keeps the prototype's English screen; it is product imagery, and the narrow-width crops are anchored to keep the canopy sign and the car rather than that screen.

## Brand assets

Every lion is derived from `../mobile/assets/icon.png`:

```bash
npm run brand
npm run check:brand
```

`scripts/generate-brand-assets.mjs` writes the website/admin tab icons, UI masks, Apple icon and Android launcher foreground. The App Store QR is generated from the canonical URL with `npm run qr`.

The tab icon is the approved drawing at **every** size, including the 16 and 32 px frames, and that
is a decision rather than an oversight. Planning #852 measured what those two frames actually look
like: the artwork's median stroke is 0.78% of its own box, so at 16 px it arrives an eighth of a
device pixel wide and 86.9% (16 px) and 84.4% (32 px) of the drawn pixels are a wash rather than a
line. A second artwork - a cut drawn for the small end - was written, measured (39.4% / 21.3%) and
merged; the owner looked at it on a screen and rejected it, because a redrawn lion is a different
lion and this artwork is approved as it is. So the drawing stays and the wash is accepted. Do not
redraw it to fix the tab: the tab is the only place that shows it that small, and a sharper mark
there costs the mark itself. `check:brand` keeps the bytes tied to the source, which is the whole
of what this file guarantees about the tab icon.

The lockup is the one brand asset that is not generated: `public/fuelflow-logo.webp` is the lion
followed by "Fuel Flow" with the accent "F" bolt as the first letter of each word. It is drawn from
the approved artwork rather than retyped, because the F is the brand's own glyph: it carries no
sidebearing, its arms overhang its stem, and the letters are sheared 13.8 degrees to sit on the
same axis as it. The numbers that hold it together are 13.8 degrees of shear, an F at 1.00 of the
cap height and 0.85 of its own width, and a clearance of 0 paint - not a padding - between the F
and the letter after it, resolved separately for `u` and `l`.

`public/fuelflow-wordmark.webp` is the same drawing cut down to the words, for the one place the
lion stops working: the phone mock's own header, where the lockup is 15px tall and the lion
collapses into a smudge. Both files carry white letters and an accent F in `#a78bfa` (the violet
`--accent-2` the palette uses), so neither can be recoloured by a CSS mask - if the accent changes,
they are re-rendered, not tinted.

`BrandLockup.tsx` is the single place that paints them: the lockup in the header, the hero and the
footer, the wordmark alone in the mock. The separate lion mask stays for the tab icon, the admin
and the launcher art, and is no longer layered next to the words - two lions from two sources were
never in register.

## Checks

`npm run check` runs against the built export:

- `check:header` — responsive navigation ladder and phone menu reachability
- `check:hero` — settled/no-JavaScript hero visibility, CTA fit, overflow, one lockup in the header and a baked-copy/ baked-accent pixel scan of the photograph
- `check:headers` — real nginx caching and 404 behavior
- `check:brand` — byte/reference parity for every lion asset
- `check:taps` — 44px mobile controls or the WCAG spacing exception on all routes
- `check:lead` — client validation and 202/400/429/5xx/offline B2B-form states plus payload shape
- `check:site` — reviewed screenshot allowlist, legacy anchors and business-lead legal disclosures
