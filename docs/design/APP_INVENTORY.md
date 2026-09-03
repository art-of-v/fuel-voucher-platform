# APP INVENTORY — FuelFlow Mobile

> Phase 1 discovery artefact. Read-only inventory of what the application actually
> contains, produced by reading every route file, every shared component and the
> design/i18n/state layers in `mobile/`. No code was modified.
>
> Scope: `mobile/` (Expo SDK ~54, React Native 0.81.5, React 19.1, expo-router ~6).
> `backend/` and `admin/` are referenced only where they explain a mobile behaviour.

---

## 1. What the product is

FuelFlow is a **prepaid fuel voucher wallet**. The user buys fuel in advance at a
fixed price (a "package" = N litres of a specific fuel at a specific station
brand), pays by card via a hosted Monobank invoice, and receives one or more
**vouchers** — each a QR code that is scanned at the pump to dispense that fuel.

Layered on top of the consumer flow is a **B2B module**: a user can register a
legal entity, sign a contract with an e-signature, invite workers, and gift
vouchers from a company pool to those workers.

So there are in fact **two products sharing one shell**:

| | Consumer (B2C) | Business (B2B) |
|---|---|---|
| Entry | Bottom tabs | Buried in Profile |
| Core loop | browse → buy → show QR at pump | register entity → sign contract → invite workers → gift vouchers |
| Screens | index, map, station, packages, basket, checkout, my-codes | profile (legal entity), contracts, company, invitations |
| Discoverability | primary | none — no tab, no badge, no onboarding |

The B2B half is roughly 40% of the screen surface and 0% of the primary
navigation. See `INFORMATION_ARCHITECTURE.md`.

---

## 2. Route inventory

15 route files, all under `mobile/app/`. Confirmed complete
(`find mobile/app -name "*.tsx"` → 15 results).

| # | Route | File | LOC | Purpose | In bottom tabs |
|---|---|---|---|---|---|
| 1 | `_layout` | [_layout.tsx](../../mobile/app/_layout.tsx) | — | Root: font loading, theme, app-lock gate, `Stack`, `BottomTabs` | n/a |
| 2 | `/` | [index.tsx](../../mobile/app/index.tsx) | — | Home. Station list + search + brand filter | ✅ Home |
| 3 | `/landing` | [landing.tsx](../../mobile/app/landing.tsx) | — | Unauthenticated splash + phone auth | ❌ |
| 4 | `/map` | [map.tsx](../../mobile/app/map.tsx) | — | Station map (react-native-maps + CartoDB raster tiles) | ✅ Map |
| 5 | `/station/[id]` | [station/[id].tsx](../../mobile/app/station/[id].tsx) | — | Station detail → pick a fuel | ❌ (tabs hidden) |
| 6 | `/packages` | [packages.tsx](../../mobile/app/packages.tsx) | — | Pick a litre package for the chosen fuel | ❌ (tabs hidden) |
| 7 | `/basket` | [basket.tsx](../../mobile/app/basket.tsx) | 191 | Cart, promocode, totals | ✅ Cart |
| 8 | `/checkout` | [checkout.tsx](../../mobile/app/checkout.tsx) | — | Auth-if-needed, create order, open Monobank | ❌ (tabs hidden) |
| 9 | `/payment-result` | [payment-result.tsx](../../mobile/app/payment-result.tsx) | — | Deep-link return from the payment provider | ❌ (tabs hidden) |
| 10 | `/my-codes` | [my-codes.tsx](../../mobile/app/my-codes.tsx) | 591 | Orders + vouchers wallet, QR access | ✅ Codes |
| 11 | `/profile` | [profile.tsx](../../mobile/app/profile.tsx) | — | Identity, theme, language, security, legal entity, links out to B2B | ✅ Profile |
| 12 | `/report` | [report.tsx](../../mobile/app/report.tsx) | 435 | Spend report, monthly breakdown, payments/redemptions log | ❌ |
| 13 | `/company` | [company.tsx](../../mobile/app/company.tsx) | — | Workers: invite / fire / gift / recall | ❌ |
| 14 | `/contracts` | [contracts.tsx](../../mobile/app/contracts.tsx) | 509 | B2B contract list + e-signature | ❌ |
| 15 | `/invitations` | [invitations.tsx](../../mobile/app/invitations.tsx) | 228 | Accept / decline worker invitations | ❌ |

**Navigation shape:** a flat `Stack` (no nested stacks, no native tab navigator).
The bottom bar is a hand-rolled overlay component, not a router-level tabs layout.

### 2.1 Screens reachable only from a link inside another screen

`/report`, `/company`, `/contracts`, `/invitations` — all four are B2B or
analytics, all four hang off `/profile` as list rows. There is no way to reach
them from Home, and nothing on Home indicates they exist.

### 2.2 Screens where the bottom bar disappears

`mobile/src/components/bottom-tabs.tsx:26`

```ts
const hidePrefixes = ['/station/', '/packages', '/checkout', '/payment-result'];
```

The purchase funnel is a hidden-chrome flow, but `/basket` — which is *inside*
the same funnel — keeps the bar. So the funnel is: tabs → no tabs → tabs → no
tabs. Chrome flickers in and out mid-purchase.

---

## 3. Feature inventory

### 3.1 Authentication & security
- Phone number + 6-digit SMS OTP (`/landing`, and again inside `/checkout`).
- Device registration with a hardware-backed key
  (`SecurityService.setupDeviceSecurity` / `signPayload`, `expo-secure-store`).
- Optional biometric unlock + app lock, gated in `_layout.tsx`.
- **Two complete, parallel auth implementations, both live:**
  - `mobile/src/components/phone-auth.tsx` (406 LOC) — used by `landing.tsx:3`
  - `mobile/src/features/auth/components/PhoneAuthForm.tsx` (331 LOC) — used by `checkout.tsx:15`
  They render differently, style differently, and have different bugs. See
  `DESIGN_PROBLEMS.md` §2.

### 3.2 Station discovery
- Station list with text search and brand filter chips (`index.tsx`).
- Map view with markers, a selected-station callout, "BUILD ROUTE" hand-off to
  the OS maps app (`map.tsx`).
- Station detail: address, brand, fuel list (`station/[id].tsx`).
- List and map are **separate destinations**, not two views of one screen; state
  is not shared between them.

### 3.3 Purchase
- Fuel selection (`FuelCard`) → package selection (`PackageCard`) → cart
  (`CartItemCard`) → checkout.
- Cart is persisted to AsyncStorage, **including** `selectedStation`,
  `selectedFuel`, `selectedPackage` (`cartStore.ts`), so a partially-configured
  purchase and its prices survive app restarts indefinitely.
- Promocodes are **client-side and shipped in the bundle**:
  `mobile/src/features/cart/types/index.ts:16`
  ```ts
  export const PROMO_CODES: Record<string, number> = { FUEL10: 10, SAVE15: 15, POWER20: 20, LEMBERG25: 25 };
  ```
- Payment: order created server-side, then `Linking.openURL` to a Monobank
  hosted invoice; fulfilment arrives by webhook; the app returns via the
  `payment-result` deep link.

### 3.4 Voucher wallet (`/my-codes`)
- Three sections: pending orders, fulfilled orders, unassigned vouchers.
- Orders render as expandable `OrderCard`s containing `VoucherCard`s.
- Tapping a voucher opens `VoucherDetailModal` with the QR.
- Voucher classification (`classifyVoucher`, `core/types/api.ts`):
  `personal` · `company_pool` · `gifted_to_me` · `gifted_to_worker` · `blocked`.
- Manual "mark as used" / "restore" toggle — a *self-reported* redemption state
  that sits alongside the real server-side redemption.

### 3.5 Reporting (`/report`)
- Period filter (all / year / 3 months / month).
- 4 summary tiles: total spent, purchased, used, litres used.
- Monthly breakdown table.
- Collapsible payments and redemptions logs.
- Native `Share.share()` export as plain text.

### 3.6 B2B
- Legal-entity profile (created in `/profile`).
- Contract list + in-app e-signature (`/contracts`, `SignaturePad`).
- Worker roster: invite, fire, gift voucher, recall voucher (`/company`).
- Inbound invitation inbox (`/invitations`).

### 3.7 Cross-cutting
- **8 themes** with a hard split in shape language (see §5).
- **4 languages** (uk / en / de / es), 235 keys, all in sync.
- Haptics wrapper used inconsistently (some presses, not others).
- `allowFontScaling={false}` applied globally — OS text-size settings are
  disabled app-wide.

---

## 4. Component inventory

### 4.1 Shared primitives — `mobile/src/core/ui/`

| Component | Status | Notes |
|---|---|---|
| `Button` | ✅ live | The only real CTA primitive. Adopted by ~4 screens; everywhere else raw `Pressable`. Filled-label contrast is inverted vs `PhoneAuthForm`'s icon colour (see `DESIGN_PROBLEMS.md` §3). |
| `MeshBackground` | ✅ live | Decorative honeycomb/mesh overlay. |
| `ErrorBoundary` | ✅ live | Hardcoded English `"Something went wrong"` / `"RETRY"`. |
| `LoadingIndicator` | ⚠️ **orphaned** | Exported from `core/ui/index.ts`, never imported. Screens use raw `ActivityIndicator`. |
| `PressableScale` | ⚠️ **orphaned** | Imported by `FuelCard.tsx:6` but never used in its JSX. |
| `ScreenHeader` | ⚠️ **near-orphaned** | Consumed by exactly one screen (`station/[id].tsx:38`). The other 13 screens hand-roll their header. |

**There is no shared primitive for:** header, card, sheet/modal, toast/inline
feedback, stat tile, empty state, section header, list row, chip/segment,
stepper, badge, price. Each screen re-invents them. This is the single largest
structural cause of the "many screens built independently" feeling.

### 4.2 Layout
- `page-layout.tsx` — `PageLayout`: safe area (`edges={['top','left','right']}` —
  **no bottom edge**), fixed background, optional header, scroll body with a
  hardcoded `paddingBottom: 150`, optional fixed footer. Because the layout owns
  no bottom inset, every footer re-adds its own magic number
  (`basket.tsx` `paddingBottom: 84`, others 100/120).
- `bottom-tabs.tsx` — floating overlay bar, `position: absolute`, `bottom: 8`,
  `height: 64`, `zIndex: 100`. Icon-only, **no labels**. No safe-area inset.

### 4.3 Feature components

| Component | LOC | Used by |
|---|---|---|
`StationCard` | 195 | index |
`FuelCard` | 253 | station/[id] |
`PackageCard` | 409 | packages |
`CartItemCard` | 146 | basket |
`OrderCard` | 397 | my-codes |
`VoucherCard` | 353 | OrderCard only |
`VoucherBadge` | 50 | my-codes, VoucherDetailModal |
`VoucherDetailModal` | 381 | my-codes |
`QrFullscreenModal` | 276 | ⚠️ **nothing — dead code** |
`SignaturePad` | 120 | contracts |
`GlowText` | 132 | basket, others |
`GridBackground` | 120 | PageLayout default, report |
`phone-auth` | 406 | landing |
`PhoneAuthForm` | 331 | checkout |

### 4.4 Dead / orphaned code (grep-confirmed)

| Item | Where | Note |
|---|---|---|
`QrFullscreenModal` | `src/components/` | 276 LOC, never imported. **It is the better QR implementation** — local SVG generation, `SCREEN_WIDTH * 0.78`, `borderRadius: 16`, no laser overlay. The version actually shipped is the worse one. |
`LoadingIndicator` | `core/ui/` | never imported |
`PressableScale` | imported by `FuelCard` | never rendered |
`formatCurrency` | `core/utils/formatters.ts` | never called; 15 raw `₴` interpolations exist instead |
`truncateId` | `core/utils/formatters.ts` | never called; `OrderCard` hand-rolls `.slice(0,10)` |
`interface QuantityState` | `PackageCard.tsx` | unused type |
`BRAND_COLORS` import | `PackageCard.tsx` | unused import |
`useWindowDimensions()` | `contracts.tsx` | destructured, never read |
`diagResult` | `useLogin.ts` | returned, never rendered |
`footerContainer.backgroundColor` | `page-layout.tsx` | overridden by the inline style on the same element |

Nine orphans is not a tidiness problem; it is a **map of amputated features**.
Three of them (`QrFullscreenModal`, `LoadingIndicator`, `PressableScale`) are the
*good* versions of things the app does badly elsewhere.

---

## 5. Design system inventory

### 5.1 Tokens — `mobile/src/core/design/tokens.ts`
`baseTokens` + `getTokens(theme)`; exposes `colors`, `spacing`, `surface`,
`text`. `BRAND_COLORS` maps fuel brands to accent colours.

### 5.2 Themes — `mobile/src/core/design/themes.ts`
**8 themes**, split by a boolean `soft` flag:
- `soft: false` → sharp "HUD" language, `borderRadius: 2`
- `soft: true` → rounded language, per-theme `surface` radius overrides

Every component that cares branches inline on `tokens.surface.soft`, e.g.
`basket.tsx:49` `borderRadius: soft ? 12 : undefined`,
`Button.tsx:64` `borderRadius: tokens.surface.soft ? tokens.surface.button : 4`.

**Consequence:** 8 themes × 2 shape languages = 16 visual configurations that
must each be individually correct. None is fully resolved — dark-only
`rgba(255,255,255,0.02–0.08)` surfaces are invisible on the light themes, and
hardcoded status colours (`#22c55e`, `#F59E0B`, `#EF4444`, `#a855f7`, `#3b82f6`)
clash with brand primaries such as `lemberg`'s `#16FF00`.

### 5.3 Typography
Loaded: `Rajdhani-Regular/SemiBold/Bold`, `Inter-Regular/Bold/Black`.
**Referenced but never loaded: `Inter-Medium`** (used in `my-codes.tsx:395`,
`VoucherDetailModal.tsx:123`, and elsewhere) — silently falls back to the system
font on both platforms.

Additionally, several components set `fontWeight` with **no `fontFamily`**
(all of `basket.tsx`'s StyleSheet, all of `CartItemCard`, `my-codes`'s
`SummaryBar`) so those surfaces render in the OS system font while their
neighbours render in Rajdhani/Inter. `basket.tsx` — a core funnel screen — is
almost entirely system-font.

### 5.4 Radii actually in use
`2, 4, 6, 8, 10, 12, 16, 18, 20, 22, 28` — plus `undefined`. Almost none read
from `tokens.surface`. `contracts.tsx` alone uses 2, 4, 12, 20 and 22 in one
file.

### 5.5 Styling mechanisms in simultaneous use
1. `StyleSheet.create` (most screens)
2. Inline style objects (most screens, often on the same element)
3. NativeWind `className` (`page-layout.tsx`, a few components)
4. `cn()` utility for class merging

`page-layout.tsx:37` sets the *same* absolute-fill rules twice — once as
`className="absolute inset-0 z-0"` and once as an inline style object — which is
a fair summary of how the styling layer is used throughout.

---

## 6. Data layer inventory

### 6.1 Two coexisting fetch paradigms
- `@tanstack/react-query` — `invitations.tsx`, parts of `company.tsx`
- manual `useState` + `useEffect` + hand-rolled `loading`/`error`/`refreshing` —
  `my-codes.tsx`, `report.tsx`, `contracts.tsx`

Result: two loading conventions, two error conventions, two refresh conventions.

### 6.2 State stores (zustand + persist/AsyncStorage)
- `appStore` — auth, theme, app lock
- `cartStore` — cart, promocode, current selection
- `useI18n` — language

### 6.3 API fields fetched and discarded
`core/types/api.ts` + `getVouchers.ts:33-34`:

| Field | Status |
|---|---|
`redemptionRules` | mapped, **never rendered** — the pump-side rules the customer needs to redeem are fetched and thrown away |
`fuelSubtype` | mapped, never rendered |
`profileImageUrl` | unused |
`qrCodeUrl` | unused |

### 6.4 A status the UI cannot represent
`Order.status` includes `'REFUNDED'`. `my-codes.tsx:187-188` filters only
`PENDING_*` and `FULFILLED || PARTIALLY_REFUNDED`. **A fully refunded order is
therefore invisible** — it appears in no section, and there is no refund UI
anywhere in the app. Meanwhile `SummaryBar` counts `orders.length` (unfiltered),
so the tile says "5 ORDERS" above a list of 4 cards.

---

## 7. Internationalisation inventory

- `mobile/src/core/i18n/store.ts` — 235 keys × 4 languages (uk/en/de/es),
  verified in sync.
- `t()` falls back to the **raw key string** on a miss:
  ```ts
  translations[lang]?.[key] || translations['en']?.[key] || key
  ```
  so a typo ships as `codes.availablePayloads` rendered literally on screen.
- Only **19 of 37** `.tsx` files import `useI18n`.

### Hardcoded strings that bypass i18n entirely

| File | Hardcoded Cyrillic | Hardcoded English |
|---|---|---|
`phone-auth.tsx` | 20 | — |
`PhoneAuthForm.tsx` | 12 | — |
`formatters.ts` | 7 | — |
`useLogin.ts` | 4 | — |
`map.tsx` | 4 | `RETRY`, `ADDRESS`, `BUILD ROUTE` |
`_layout.tsx` | 3 | `RETRY LOAD` |
`station/[id].tsx` | 2 | — |
`my-codes.tsx` | — | `READY`, `⟳ REFRESH` |
`VoucherDetailModal.tsx` | — | `READY`, `REDEEMED`, `QR Unavailable` |
`VoucherCard.tsx` | — | `Ready`, `Redeemed`, `Pending`, `Blocked`, `Expired` |
`OrderCard.tsx` | — | `REFUNDED`, `UNPAID` fallback |
`CartItemCard.tsx` | — | `LITERS` |
`QrFullscreenModal.tsx` | — | `Voucher`, `REDEEMED`, `Tap anywhere to close` |
`ErrorBoundary.tsx` | — | `Something went wrong`, `RETRY` |

The **login screen** — the first screen a new user sees — is hardcoded Ukrainian.
A German or Spanish speaker cannot log in in their own language even though the
translations for the rest of the app exist.

### Product vocabulary
The Ukrainian and English translation files themselves contain military /
hacker-console vocabulary in a consumer fuel-buying app:
`codes.noAssets` → "NO ASSETS", `codes.availablePayloads` → "AVAILABLE
PAYLOADS", and `_layout`'s "SECURING DATA STREAM". This is deliberate, not a
leak — which makes it a positioning decision to review, not a bug to fix.

---

## 8. Modal / overlay inventory

Three unrelated overlay paradigms coexist:

| Paradigm | Where | Android back | Backdrop dismiss |
|---|---|---|---|
RN `Modal` (`transparent`, `fade`) | `VoucherDetailModal` | ✅ `onRequestClose` | ❌ |
`Modal` stacked on `Modal` | `contracts.tsx` (contract text over signing sheet) | partial | ❌ |
Bare absolute `Pressable` overlay | `QrFullscreenModal` (dead) | ❌ | ✅ everything, including the QR itself |
Native `Alert.alert` | ~everywhere, as the universal feedback channel | n/a | n/a |

`Alert.alert` carrying the app's success/failure feedback is a direct consequence
of §4.1: there is no toast or inline-feedback primitive, so the OS dialog is the
fallback. It is also why some actions confirm loudly and others (declining an
invitation, deleting a cart line) confirm not at all.

---

## 9. Quantified summary

| Metric | Count |
|---|---|
Route files | 15 |
Screens in bottom tabs | 5 |
Screens reachable only via Profile rows | 4 |
Distinct header implementations | 6 |
Distinct stat-tile designs | 5 |
Distinct chip/segment designs | 4 |
Overlay paradigms | 3 (+ `Alert`) |
Themes | 8 |
Shape languages | 2 (`soft` true/false) |
Border radii in use | 11 distinct values |
Live auth implementations | 2 |
Data-fetching paradigms | 2 |
Languages | 4 (235 keys) |
Files importing `useI18n` | 19 / 37 |
Dead or orphaned symbols | 10 |
API fields fetched and discarded | 4 |
Order statuses with no UI | 1 (`REFUNDED`) |
