# DESIGN PROBLEMS — the recurring patterns

> Phase 1 discovery artefact. [UX_AUDIT.md](UX_AUDIT.md) lists 81 individual
> defects. This document names the **~12 generative patterns** that produce them.
>
> The question this answers: *why does this app feel raw, generic, inconsistent and
> unfinished, when the token system, theme system and i18n system are all well
> built?*
>
> Short answer: the systems exist and the screens don't use them. Every pattern
> below is a variation on that.

---

# Phase 2 resolution status

Phase 2 established the design foundation
([`DESIGN_SYSTEM.md`](DESIGN_SYSTEM.md)) and migrated the
infrastructure-level violations. It deliberately did **not** redesign any product
flow, so patterns whose fix *is* a screen redesign remain open by design.

The pattern text below is left as written in Phase 1 — it is the diagnosis, and
rewriting it would erase the record. This table is the current state.

| # | Pattern | Status | What closed it / what remains |
| --- | --- | --- | --- |
| 1 | No component layer | **Resolved (foundation)** | `core/ui/` is now 26 components covering all 20 required roles. Adoption is partial by design — see Pattern 11. |
| 2 | The design system is bypassed rather than extended | **Resolved (rule + enforcement)** | Every raw colour outside `src/core/design/` is gone except four documented exceptions. The rule is written down (§14) and the one real gap found during validation was closed by *extending* a component (`Button size="sm"`), not by inlining a style. |
| 3 | Two shape languages × eight themes | **Resolved** | Themes no longer carry shape: the `soft` flag's effect and the per-theme `surface` radius overrides are removed. One radius scale (6/10/14/20/full), one differentiation rule (border **or** shadow). |
| 4 | The OS is doing the product's design work | **Partially resolved** | The feedback layer exists (`InlineFeedback`, `ConfirmDialog`, `Toast`, `EmptyState`, `LoadingState`, `ErrorState`) and `Select` opens a themed `BottomSheet` instead of a platform picker. **18 `Alert.alert` product call sites remain** — replacing them changes flow behaviour, so it is Phase 3. |
| 5 | Decoration applied without asking what the screen is for | **Partially resolved** | Glow is out of the system; the looping QR laser is gone; `GridBackground` and `MeshBackground` are deprecated in place with call sites listed. They are still live because deleting them changes the look of checkout, the wallet and the package cards — Phase 3. |
| 6 | Micro-typography as a substitute for hierarchy | **Resolved (foundation)** | 14-role type scale, 12px floor, tracking capped at 0.4, uppercase reduced to two roles, font scaling on with per-role caps. **126 `allowFontScaling={false}` sites remain in 14 unmigrated files.** |
| 7 | Nobody owns the layout container | **Resolved** | `PageLayout` + `useContentInsets` own safe areas, scroll, keyboard insets and bottom padding. Zero screens read `useSafeAreaInsets()`. Every magic bottom padding (150 / 120 / 100 / 84 / `bottom: 8`) and both `<View style={{ height: 100 }} />` spacers are gone. |
| 8 | Destructive actions are cheaper than safe ones | **Partially resolved** | `ConfirmDialog tone="destructive"` and `Button variant="destructive"` exist and the asymmetry rule is documented (§12). Wiring them into basket's clear-cart and the delete-account flow is a behavioural change to the purchase flow — Phase 3. |
| 9 | Product state exists in the data and not in the interface | **Open — Phase 3** | Foundation support was added (`status.neutral` for used/expired/archived, `Badge`, `Card accent`), so the states are now *expressible*. Surfacing them is screen work. |
| 10 | Two of everything, and the wrong one shipped | **Mostly resolved** | One header (11 of 12 screens), one loading state (7 hand-rolled blocks migrated), one scrim, one press primitive. Remaining duplicates are the three voucher representations and the five card components — both are Phase 3 rewrites. |
| 11 | Infrastructure finished, adoption abandoned | **Partially resolved** | i18n is 272 keys × 4 locales, verified equal; the login screen is translated and on shared components. **`Card` still has zero call sites and `Button` has four** — migrating them is the redesign Phase 2 was told not to start. |
| 12 | The product's voice describes a different product | **Open — Phase 3** | A copy pass is explicitly Phase 3. Note the new constraint it must satisfy: `ScreenHeader` renders titles in sentence case, but the title keys are still `'NETWORK MAP'`, `'PROFILE'`, `'YOUR BASKET'` in all four locales. |

**Net:** 5 patterns resolved, 5 partially resolved, 2 open. Every "partially" and
"open" entry is blocked on the same thing — it requires changing a screen, which
Phase 2 was scoped out of. See
[`PHASE_2_IMPLEMENTATION.md`](PHASE_2_IMPLEMENTATION.md) for the full backlog and
for what must not be touched yet.

---

## Pattern 1 · There is no component layer, so every screen invents one

**The evidence.** `mobile/src/core/ui/` contains six exports. Three are live
(`Button`, `MeshBackground`, `ErrorBoundary`), two are orphaned
(`LoadingIndicator`, `PressableScale`), and one is used by exactly one screen
(`ScreenHeader` → `station/[id].tsx:38`).

There is **no shared primitive** for any of:

> header · card · sheet/modal · toast · inline feedback · stat tile · empty state ·
> section header · list row · chip/segment · stepper · badge · price · divider ·
> loading state · error state

**What that produces, counted:**

| Element | Independent implementations |
|---|---|
Header | **6** (title sizes 32/28/24/18; back buttons 44×44-bordered, `padding:8`-bare, `padding:sm`-bordered-conditional-radius) |
Stat tile | **5** (`report` `summaryCard`, `my-codes` `SummaryBar`, + `company`, `profile`, `index`) |
Chip / segment | **4** (`report` `filterChip` r8, `index` brand filters, `my-codes` pills, `company` role tags) |
Overlay paradigm | **3** + `Alert.alert` (RN `Modal`; `Modal`-on-`Modal`; bare absolute `Pressable`) |
Voucher card | **3** (`VoucherCard`, the inline card at `my-codes.tsx:350-457`, `VoucherDetailModal`) |
"Used" state treatment | **3** (diagonal stamp / opacity / `BlurView`) |
Accent bar width | **3** (5px, 8px, none-with-border) |
Currency format | **4** (raw, `.toFixed(2)`, unrounded float, `toLocaleString`) |
Loading state | **2 conventions** (react-query vs manual `useState`) |
Auth screen | **2 complete implementations, both live** |

**Why it makes the app feel raw.** Three screens that each hand-roll a header will
never agree, no matter how carefully each one is built, because there is no shared
definition to agree with. Consistency here is not a discipline problem — it is
structurally impossible. The three files that independently reproduced *the same*
header anti-pattern (24px title + bare `padding: 8` chevron + a phantom
`<View style={{width:24}}/>` spacer for optical centring) prove the point: the
same person solved the same problem three times and got three slightly different
answers.

This is the single highest-leverage pattern in the document. Roughly 30 of the 81
audit findings dissolve if it is fixed.

---

## Pattern 2 · The design system is bypassed rather than extended

**The evidence.** `core/design/tokens.ts` and `themes.ts` are genuinely
well-structured — `colors`, `spacing`, `surface`, `text`, `BRAND_COLORS`, 8 themes,
per-theme radius overrides. And then:

- **11 border radii** appear in the codebase (`2, 4, 6, 8, 10, 12, 16, 18, 20, 22,
  28`) plus `undefined`. Almost none read from `tokens.surface`. `contracts.tsx`
  uses five of them in one file.
- **Status colours are hardcoded Tailwind defaults**, outside all 8 themes:
  `OrderCard` — `#EF4444`, `#F59E0B`, `#a855f7`, `#22c55e` + `rgba(34,197,94,…)`;
  `VoucherBadge` — `#22c55e`, `#a855f7`, `#3b82f6`; `bottom-tabs` — `#EF4444`.
- **`formatCurrency()` exists in `core/utils/formatters.ts` and is never called.**
  15 raw `₴` interpolations exist instead, 6 without rounding.
- **`truncateId()` exists and is never called.** `OrderCard` hand-rolls
  `(order.id||'').slice(0,10).toUpperCase()`.
- **Dark-only surfaces**: `rgba(255,255,255,0.02 / 0.05 / 0.08 / 0.1)` used for
  card backgrounds, tiles, borders and pills — invisible or inverted on the three
  light themes.

**The consequence that hurts most.** On the `lemberg` theme, whose brand primary is
`#16FF00`, an order's "fulfilled" state renders in `#22c55e`. Two greens, one
screen, neither related. It reads as a rendering bug, not a palette.

**Why it makes the app feel raw.** The system was built, and then each screen
solved its own problem locally with a literal. So the app has all the *machinery*
of consistency and none of the *result*. This is worse than having no system, because
the existence of `formatCurrency` next to `113.05000000000001 ₴` on the checkout
path is the clearest possible signal that nobody is enforcing anything.

---

## Pattern 3 · Two shape languages, multiplied by eight themes

**The evidence.** `themes.ts` splits every theme by a boolean `soft` flag:
`soft: false` → sharp HUD, `borderRadius: 2`; `soft: true` → rounded, with
per-theme radius overrides. Every component that cares branches **inline**:

```tsx
// basket.tsx:49
borderRadius: soft ? 12 : undefined
// core/ui/Button.tsx:64
borderRadius: tokens.surface.soft ? tokens.surface.button : 4
// basket.tsx StyleSheet, same elements
backButton: { borderRadius: 4 }   // ← and the inline override above
```

**8 themes × 2 shape languages = 16 visual configurations**, each of which must be
individually correct. And they are branched *per call site*, so the branch is
re-decided, differently, in every file — sometimes to `12`, sometimes to
`tokens.surface.button`, sometimes to `undefined` on top of a StyleSheet value
of `4`.

**The proof that it does not hold together.** `contracts.tsx` cards are
`borderRadius: 2` (sharp). `invitations.tsx` cards are `12` and its buttons `10`
(rounded). These are **sibling B2B screens, one tap apart**, in opposite shape
languages, on the same theme, at the same time.

**Why it makes the app feel raw.** No theme is fully resolved, because resolving
one means auditing 37 files. The `soft` flag was a way to add a second visual
direction without committing to it, and the cost is that neither direction is
finished. A product with one confident visual language beats a product with eight
themes and two half-languages, every time.

---

## Pattern 4 · The OS is doing the product's design work

**The evidence.**

- **`Alert.alert` is the app's feedback system.** Success, failure, validation and
  gating all arrive as OS dialogs, because `core/ui/` has no toast or
  inline-feedback primitive (Pattern 1). It appears in nearly every screen.
- **`Share.share()`** is the entire export feature of `/report` — a plain-text
  message, from the richest data model in the app.
- **Raw `ActivityIndicator`** on every loading screen, while `LoadingIndicator`
  sits orphaned in `core/ui/`.
- **`Linking.openURL`** hands payment to a hosted web page.
- **Unicode glyphs standing in for icons**: `▲`/`▼` at 12px in `report.tsx:338,372`
  and `'⟳ REFRESH'` in `my-codes.tsx` — in an app that imports
  `lucide-react-native` on both of those screens.
- **`fontWeight` with no `fontFamily`** across `basket.tsx`'s entire StyleSheet,
  all of `CartItemCard`, and `my-codes`'s `SummaryBar` — so those surfaces render
  in San Francisco / Roboto. **The cart, a screen in the paid conversion path,
  renders in the OS system font** while the screens either side of it render in
  Rajdhani.
- **`Inter-Medium` is referenced and never loaded** — an invisible third typeface,
  silently the system font.

**Why it makes the app feel generic.** Every one of these is a place where the
product declined to have a voice and let the platform speak instead. Individually
they are shortcuts; collectively they mean that at the emotional peaks of the
experience — payment succeeded, code applied, invitation accepted, cart reviewed —
the user is looking at iOS, not at FuelFlow.

The single most telling instance: an app with two custom typefaces, eight themes
and a bespoke HUD aesthetic delivers its cart in the system font and its expand
chevrons as `▲`.

---

## Pattern 5 · Decoration applied without asking what the screen is for

**The evidence.**

- **A 2px `#DC2626` line loops permanently across the customer's own QR code**
  (`VoucherDetailModal.tsx:15-54`). A fake scanner laser, drawn over the one
  surface in the product that must be machine-readable, at the one moment the user
  cannot troubleshoot.
- **`GridBackground`** renders a HUD grid behind every screen via `PageLayout`'s
  default, including the cart and the contract signing sheet.
- **`MeshBackground`** adds a honeycomb overlay on top of that, inside cards.
- **`GlowText`** with `intensity="high"` on the basket total.
- **Press tilt at three magnitudes for one gesture** across three consecutive
  funnel screens: `3deg` (`StationCard`) → `8deg` (`FuelCard`) → `5deg`
  (`PackageCard`). `8deg` reads as a glitch.
- **Three "used" treatments** for one state: a rotated `diagonalStamp`, an
  `opacity: 0.5`, and a `BlurView`.
- **`activeGlow`** — the bottom tab's active state is a `primaryDim` shadow blob
  behind an unlabelled icon, instead of colour + label.

**Why it makes the app feel raw.** Polish is not the amount of visual effect; it is
the *ratio* of effect to purpose. Here the ratio inverts on the most important
screen: the QR gets a laser (harmful), a mesh, a grid, and a 32×32 close button
(too small), while `redemptionRules` — the information the user actually needs at
the pump — is fetched and discarded.

A designer looking at the voucher modal would conclude that nobody asked what a
person standing at a fuel pump needs. The laser is the clearest single artefact in
the audit: it is effort spent in exactly the wrong direction.

---

## Pattern 6 · Micro-typography as a substitute for hierarchy

**The evidence.**

- Label sizes in constant use: **8, 9, 10, 11, 12px**, uppercase, with
  `letterSpacing` between **0.5 and 6**. `report.tsx` `summaryLabel` is
  `fontSize: 8, letterSpacing: 1.5`; `sectionTitle` is `fontSize: 12,
  letterSpacing: 4`; `VoucherBadge` is 9px `Inter-Black`; `OrderCard`'s PAY label
  is 9px.
- `allowFontScaling={false}` applied **globally**, so OS text-size settings do
  nothing.
- **Fixed card heights** (`104`, `118`, `64`) with `numberOfLines={1}`.
- Title sizes across screens: **32 / 28 / 24 / 18** — four values, no scale, no
  relationship.
- **Hierarchy inverted where it matters most**: `FuelCard` renders the fuel *name*
  at 16px in `text.dim` and the *price* at 28px in the brand colour, on the screen
  whose only job is choosing a fuel.
- `fontWeight` + `fontFamily` conflicts (`modalIdText`: `'Inter'` + `'700'`;
  `stepperValue`: `'Rajdhani-Bold'` + `'900'`) where one silently wins.

**Why it makes the app feel raw.** Uppercase 9px tracked labels are a *texture*,
not a hierarchy — they all read at the same level regardless of importance, so the
eye gets no guidance. Then, because everything is small and tracked, differences
have to be expressed by adding more effects (colour, glow, borders, badges), which
adds noise, which further flattens the hierarchy.

The accessibility consequence is not incidental: an app that disables text scaling
globally and then sets its labels at 8px has decided that users who need larger
text are not users.

---

## Pattern 7 · Nobody owns the layout container, so everyone guesses

**The evidence.**

- `PageLayout` declares `edges={['top','left','right']}` — **no bottom inset** —
  and hardcodes `contentContainerStyle: { paddingBottom: 150 }`.
- `bottom-tabs.tsx` is `position: 'absolute'`, `bottom: 8` hardcoded, `height: 64`,
  and **never applies `useSafeAreaInsets()`** despite importing it.
- So screens add their own: `basket.tsx:154` `paddingBottom: 100` on the content
  **and** `footer: { paddingBottom: 84 }`; `report.tsx:215` `paddingBottom: 100`;
  others 120.
- `disableScroll` skips the 150 entirely, so those screens put content under the
  floating bar.
- `map.tsx` applies a safe-area inset **on top of** `PageLayout`'s → double top
  padding.
- `zIndex` is negotiated with overlapping magic numbers: tabs `100`, header `40`,
  footer `50`, dead `QrFullscreenModal` `100`.
- Arithmetic appears in style values to compensate for absolutely-positioned
  ornaments: `my-codes.tsx:384` `padding: 22, paddingLeft: 22 + 5 + 16`;
  `VoucherDetailModal.tsx:263` `padding: 24, paddingLeft: 24 + 8 + 12,
  paddingRight: 12 + 32`. Two cards, two arithmetic schemes for the same idea.

**Why it makes the app feel unfinished.** Magic numbers stack: ~250px of dead space
above the fold on some screens, content clipped under the tab bar on others, and a
tab bar sitting inside the home-indicator gesture zone. Vertical rhythm cannot
exist when four different files each add an unlabelled number to the bottom of the
same scroll view.

`page-layout.tsx:37` is the pattern in miniature — the *same* absolute-fill rules
declared twice on one element, once as `className="absolute inset-0 z-0"` and once
as an inline style object.

---

## Pattern 8 · Destructive actions are consistently cheaper than safe ones

**The evidence.**

| Action | Cost to the user | Confirmation |
|---|---|---|
Delete a cart line | one tap on "−" at quantity 1 | **none** — `cartStore` filters it out silently |
Clear the entire cart | one tap on 10px red text | **none** |
Decline a company invitation (irreversible) | one tap | **none** — haptic only |
Sign a binding supply contract | one tap on an unlabelled 40×40 circle | **none** — no checkbox, no scroll gate |
Accept an invitation (safe) | one tap | ✅ `Alert` |
Apply a promocode (trivial) | typing + a bordered button | ✅ visible state |
Read the contract you are signing | a dashed secondary button → a **second modal on top of the first** | — |

**Why it happens.** Pattern 1 again: with no confirmation primitive, each call site
decides independently whether to add an `Alert.alert`. The result correlates with
*when the code was written*, not with *how dangerous the action is*.

**Why it makes the app feel untrustworthy.** In `invitations.tsx` the destructive
button gives **less** feedback than the safe one — the safe path confirms, the
irreversible path is silent. A user cannot build a model of which taps are safe,
so every tap carries residual anxiety. That is the mechanism by which an app feels
"not solid" even when it functions.

---

## Pattern 9 · Product state exists in the data and not in the interface

**The evidence.**

| Server-side truth | UI representation |
|---|---|
`Order.status = 'REFUNDED'` | 🔴 **none** — matches no filter, renders in no section, the order **vanishes** |
`Order.status = 'PARTIALLY_REFUNDED'` | an `#a855f7` accent, no copy |
`voucher.redemptionRules` | 🔴 **none** — fetched, mapped, discarded |
`voucher.fuelSubtype` | none — fetched, discarded |
`profileImageUrl`, `qrCodeUrl` | none — unused |
voucher `used` vs `active` | **the same colour** (`brandColor`); distinguished only by a label and opacity |
promocode invalid *why* | two states only: accepted, or red border |
an inbound invitation | 🔴 no badge, no push, no banner anywhere |
payment cleared / voucher issued | no notification — pull-to-refresh only |
voucher recalled by employer | it silently disappears |

**Why it makes the app feel unfinished.** The domain model is richer than the
interface, in both directions: the UI throws away information the user needs
(`redemptionRules` at the pump) and fails to represent states the backend
explicitly supports (`REFUNDED`).

The refund case is the sharpest: money left the user's account, an order existed,
and now the order renders nowhere while a stat tile still counts it — so the
wallet displays "5 ORDERS" above four cards. That is not a missing feature; it is
the interface contradicting itself in front of the user.

---

## Pattern 10 · Two of everything, and the wrong one shipped

**The evidence.**

| Thing | Version A | Version B | Which ships |
|---|---|---|---|
Auth screen | `phone-auth.tsx` (406 LOC, raw `Pressable`, `borderRadius: 2`, never soft, leaks crypto diagnostics **in production**) | `PhoneAuthForm.tsx` (331 LOC, uses shared `Button`, token radii) | **both** — A on `/landing`, B on `/checkout` |
QR display | `VoucherDetailModal` — **remote** 220×220 `Image`, `#666` "QR Unavailable" fallback, **no retry**, laser overlay | `QrFullscreenModal` — **local SVG**, `SCREEN_WIDTH * 0.78`, `borderRadius: 16`, no laser | **A**. B is dead code, never imported |
Loading state | raw `ActivityIndicator`, per screen | `LoadingIndicator` in `core/ui/` | **A**. B orphaned |
Press feedback | inline `transform: pressed ? [{scale:0.97}] : []` | `PressableScale` (imported by `FuelCard.tsx:6`) | **A**. B imported but never rendered |
Currency | 15 raw `₴` interpolations, 4 formats | `formatCurrency()` in `formatters.ts` | **A**. B never called |
Order ID | `(order.id||'').slice(0,10).toUpperCase()` | `truncateId()` in `formatters.ts` | **A**. B never called |
Header | 6 hand-rolled | `ScreenHeader` in `core/ui/` | **A** ×13. B used once |
Data fetching | manual `useState`/`useEffect` | react-query | **both** |
Auth boolean | `appStore.isAuthenticated` | `useAuth().isAuthenticated` | **both**, OR'd together at the call site |

**Why it is the most diagnostic pattern in the audit.** Three times, the team built
the *better* implementation and shipped the *worse* one. `QrFullscreenModal` is not
merely nicer — it is the correct design for the product's most important surface
(offline-capable, large, no laser), and it is 276 lines of dead code while the
remote-image version with a red laser is what users hold up at the pump.

This is what "unfinished" actually means here. Not that the work wasn't done — that
it was done twice, and the migration was never completed. Ten orphaned symbols are
not a tidiness issue; they are a **map of half-finished migrations**, and every one
of them is a place where the product's quality is worse than the repository's.

---

## Pattern 11 · Infrastructure finished, adoption abandoned

**The evidence.** Three complete systems, each bypassed by the majority of the
code that should use it:

| System | Completeness | Adoption |
|---|---|---|
i18n | ✅ **235 keys × 4 languages, verified in sync** | **19 of 37** files import `useI18n` |
Tokens / themes | ✅ well-structured, 8 themes | 11 hardcoded radii, ~10 hardcoded hexes, dark-only `rgba()` surfaces |
`Button` | ✅ sound primitive | ~4 screens; everywhere else raw `Pressable` |

The i18n case is the starkest. The infrastructure is *finished*. And:

- The **login screen** — the first screen every user sees — has **20 hardcoded
  Ukrainian strings**. A German or Spanish speaker cannot log in in their own
  language, and the language selector is behind `/profile`, which requires login.
  It is a closed loop.
- `t()` falls back to the **raw key**, so a typo ships as
  `codes.availablePayloads` rendered literally.
- Mixed i18n *inside single expressions*:
  `{isBlocked ? t('voucher.badge.blocked') : isUsed ? 'REDEEMED' : 'READY'}` —
  one ternary, one translated branch, two hardcoded.
- `VoucherCard` imports `useI18n` and translates exactly one string while
  `getStatusConfig` returns hardcoded English `'Ready'/'Redeemed'/'Pending'/
  'Blocked'/'Expired'`.
- `formatters.ts` — a **utility module** — carries 7 hardcoded Cyrillic UI strings.

  > **Correction (Phase 2).** This finding was wrong and is retracted. The seven
  > Cyrillic strings in `formatters.ts` are the *lookup keys* of `fuelNameMap`
  > inside `normalizeFuelName`, which maps the fuel names the station feeds send
  > onto the app's canonical identifiers (`'газ'` → `'gas'`). They are API input
  > values, not UI copy, and translating them would break the mapping. The source
  > now carries a docblock saying so. The rest of Pattern 11 stands.

**Why it makes the app feel raw.** Half-adopted infrastructure is worse than none,
because it creates the *appearance* of a supported capability that fails at the
first real test. A user who switches to English still meets `READY`, `LITERS`,
`RETRY`, `ADDRESS`, `BUILD ROUTE`, `QR Unavailable`, `Something went wrong` and
`⟳ REFRESH`.

---

## Pattern 12 · The product's voice describes a different product

**The evidence.** This is not a leak — the strings are in the **translation
files**, deliberately:

- `codes.noAssets` → **"NO ASSETS"**
- `codes.availablePayloads` → **"AVAILABLE PAYLOADS"**
- `_layout` boot → **"SECURING DATA STREAM"**

Supported visually by: a permanent HUD `GridBackground` behind every screen, a
honeycomb `MeshBackground` inside cards, `GlowText` on totals, an animated
scanner laser over the QR, all-uppercase tracked micro-labels, and Rajdhani
(a technical/motorsport display face) as the primary typeface.

**Why it matters.** A person buying 40 litres of diesel for their car is not
managing assets or payloads, and telling them their data stream is being secured
does not reassure them — it makes a fuel purchase feel like an unfamiliar system.

The gap is not that the aesthetic is bad. Rajdhani, the brand accents and the dark
themes are a coherent and defensible direction for a fuel/motorsport product. The
problem is that the **vocabulary and ornament are running ahead of the product's
actual job**, while the job itself — show me my code, tell me how to redeem it,
tell me if I've been refunded — is under-served. The app talks like a tactical
console and behaves like an unfinished wallet.

---

# Root-cause map

The 81 audit findings trace back to five decisions:

### 1. `core/ui/` was never populated → **Pattern 1**
The one decision with the widest blast radius. Six headers, five stat tiles, four
chips, three modal paradigms, three voucher cards, `Alert.alert`-as-feedback,
inconsistent confirmations (Pattern 8) and most of the "many screens built
independently" feeling all follow from this.

### 2. A second visual direction was added without retiring the first → **Patterns 3, 10**
The `soft` flag, `phone-auth` vs `PhoneAuthForm`, `VoucherDetailModal` vs
`QrFullscreenModal`, `ActivityIndicator` vs `LoadingIndicator`, react-query vs
manual fetching, two `isAuthenticated` sources. Every migration was started and
none was finished, so the app permanently contains both answers to six questions.

### 3. Systems were built but adoption was never enforced → **Patterns 2, 11**
Tokens, themes, i18n and `Button` are all sound. Nothing prevented a screen from
hardcoding `#22c55e`, `borderRadius: 12`, or `'READY'`. `formatCurrency` sitting
unused next to `113.05000000000001 ₴` on the checkout path is the emblem of this.

### 4. Screens were designed as compositions of effects, not as answers to user questions → **Patterns 5, 6, 12**
The voucher modal has a laser, a mesh, a grid, a glow and a 32px close button, and
does not render `redemptionRules`. `FuelCard` makes the price 28px and the fuel
name 16px dim. `PackageCard` gives five options five identical maximum-emphasis
CTAs. The visual layer was iterated; the information design was not.

### 5. The interface was never reconciled with the domain model → **Pattern 9**
`REFUNDED` exists in the API and nowhere in the UI. `redemptionRules` is fetched
and discarded. Every significant event in this product is server-driven and there
is no notification layer at all. The UI represents the happy path of the data
model and nothing else.

---

# The one-sentence version

The app does not feel raw because it lacks visual effort — it has more visual
effort than it needs. It feels raw because **there is no component layer, so no two
screens can agree; two of everything was built and the worse one shipped; and the
information design of the two screens that matter most — buy and redeem — was never
done at all.**
