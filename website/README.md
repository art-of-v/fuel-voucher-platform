# FuelFlow — marketing site

Static export of the FuelFlow marketing site, served at `palne.shop`. Built with Next.js (App
Router) + TypeScript.

The `lf-` class prefix and the "Lemberg palette" name in `globals.css` are not a stale brand: they
come from `lemberg`, the default theme id in `mobile/src/core/design/themes.ts`. The product name
every visitor sees is FuelFlow, and that is what `layout.tsx` puts in the metadata.

## Stack

- **Framework**: Next.js 16 (App Router) with `output: 'export'`
- **Styling**: CSS Modules + custom design tokens in `globals.css` (no Tailwind, no UI libraries)
- **Fonts**: Inter (body) and Inter Tight (display), self-hosted as four woff2 subsets. Declared by
  hand as `@font-face` at the top of `globals.css`, **not** through `next/font` — see the comment
  there for why both choices are deliberate
- **Animation**: pure CSS, IntersectionObserver-based reveals, `prefers-reduced-motion` respected

## Develop

```bash
npm install
npm run dev          # http://localhost:3000  (runs `npm run qr && npm run brand` first)
npm run lint
```

## Build for production

```bash
npm run build
```

The static export is written to `./out/`. `website/Dockerfile` copies that directory into an nginx
image and that image is what runs in production — the site is not deployed by hand-copying `out/`
onto a box.

## Project structure

`src/app/` holds the routes — `/`, `/privacy`, `/terms`, `/support` — plus `layout.tsx` (metadata,
Open Graph, structured data) and `globals.css` (the `@font-face` block, the tokens, the shared
utilities). `page.tsx` composes the homepage from the section components.

`src/components/` is one directory of section components and their CSS Modules. `Header`, `Hero`,
`How`, `Numbers`, `AppShowcase`, `Prices`, `Partners`, `Business`, `Gallery`, `Manifesto`, `FAQ`,
`Final`, `Contact`, `Footer` are the homepage sections in document order; `Effects`, `Marquee`,
`PhoneMock`, `AppleGlyph`, `AppStoreBadge`, `StoreQrCard`, `ScrollProgress`, `SectionIndex` and
`SupportClient` are the pieces they share.

`public/` is the static export's root: the hero photo, the brand mark (`lion-mask.png`), the App
Store QR code (`app-store-qr.svg`, written by `npm run qr`), the four font subsets under `fonts/`,
and the photography the gallery and the business section use. What a file means is worth reading in
the component that asks for it, not here — this directory has not been the two-item list an earlier
version of this file claimed.

## Design system

Tokens live in `src/app/globals.css`:

- `--bg` `#000000` (also the `<body>` background and `viewport.themeColor`), `--bg-2` `#0c0c0c` and
  `--bg-3` `#161616` for the surfaces above it
- `--accent` `#00e85f` — the neon green of the lion, with `--accent-pressed`, `--accent-subtle` and
  `--accent-border` derived from it, and `--accent-on` `#052e14` as the text colour on top
- `--accent-2` `#8b5cf6` — the purple used for the app/digital signal, used sparingly
- `--font-display` (Inter Tight) for headings, `--font-sans` (Inter) for everything else. There is
  no mono token; tabular figures come from `font-variant-numeric` on the sans

Sections take `lf-section` for the rhythm, plus `lf-section--alt` (`--bg-2`, used by `Partners`) or
`lf-section--deep` (`--bg`, used by `How`, `Business`, `SupportClient` and the privacy/terms pages).

## Brand assets

Every lion the site ships is derived from one file: `../mobile/assets/icon.png`.

```bash
npm run brand        # writes them; the output is committed
npm run check:brand  # rebuilds them and fails if the committed bytes differ
```

`scripts/generate-brand-assets.mjs` writes `src/app/favicon.ico`, `src/app/apple-icon.png` and
`public/lion-mask.png`. The same derivation also supplies the admin's tab icon and UI mask and the
Android launcher foreground, so the whole platform paints one lion — see the header of
`scripts/lib/brand-assets.mjs` for how each surface is derived and why the choices are what they
are, and the header of `scripts/generate-brand-assets.mjs` for why this is not a `prebuild` hook.

## Checks

`npm run check` runs after the build, in CI, and gates what a reviewer cannot see in a diff:

- `check:header` — the responsive ladder and the burger's tap target
- `check:hero` — the hero actually paints, including with JavaScript disabled
- `check:headers` — the cache headers `nginx.conf` ships are the ones the export needs
- `check:brand` — every brand surface is still the lion, is still shipped by the export, and is
  still referenced by the file that loads it
- `check:taps` — every standalone control on a phone clears a 44x44 target

## Deployment

`deploy/docker-compose.prod.yml` runs the `website/Dockerfile` image and `deploy/Caddyfile`
reverse-proxies `palne.shop` and `www.palne.shop` to it; staging is
`deploy/docker-compose.staging.yml` on the same pipeline. Both images are built by the CI job in
`.github/workflows/ci.yml`, not by anything on the box.
