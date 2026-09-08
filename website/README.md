# LEMBERG-FUEL — marketing site

Static export of the LEMBERG-FUEL marketing site. Built with Next.js (App Router) + TypeScript.

## Stack

- **Framework**: Next.js 16 (App Router) with `output: 'export'`
- **Styling**: CSS Modules + custom design tokens in `globals.css` (no Tailwind, no UI libraries)
- **Fonts**: Fraunces (display serif), Inter (sans), JetBrains Mono (mono) — loaded via `next/font/google`
- **Animation**: pure CSS, IntersectionObserver-based reveals, `prefers-reduced-motion` respected

## Develop

```bash
npm install
npm run dev          # http://localhost:3000
npm run lint
```

## Build for production

```bash
npm run build
```

The static output is written to `./out/` and contains an `index.html`, hashed JS/CSS chunks, and the static assets. Deploy the contents of `./out/` directly to a CDN, nginx, Caddy, or any static-file web server.

## Project structure

```
src/
  app/
    layout.tsx       # fonts, metadata, OG, structured data
    page.tsx         # homepage composition
    globals.css      # design tokens + global utilities
  components/
    Header.tsx       # sticky header + mobile menu
    Hero.tsx         # editorial hero
    About.tsx        # product comparison (card vs. voucher)
    How.tsx          # 4-step process timeline
    Prices.tsx       # networks grid with discounts
    FAQ.tsx          # accordion
    Contact.tsx      # form + details
    Footer.tsx       # multi-column footer
public/
  hero-bg.png        # hero photo
```

## Design system

Tokens live in `src/app/globals.css`:

- `--bg` / `--bg-deep`: cream and deep-charcoal surfaces
- `--accent`: a restrained amber (fuel nod, not neon)
- `--font-display`: serif (Fraunces) for headings and editorial moments
- `--font-sans`: Inter for UI and body
- `--font-mono`: JetBrains Mono for labels and tabular numbers

Sections opt into `lf-section--dark` for the deep-charcoal inversion used on `How`, `Contact`, and `Footer`.

## Deployment

This site is intended for `palne.shop` and serves from `/root/site/site/` on the production server. See `../../deploy/Caddyfile` for the reverse-proxy config.