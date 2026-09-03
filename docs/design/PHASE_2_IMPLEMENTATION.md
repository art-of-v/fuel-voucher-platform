# Phase 2 — Design Foundation Implementation Report

**Scope:** establish the design foundation. **Not** redesign the product.
Checkout, the wallet, the QR redemption flow, navigation and the purchase flow are
untouched by design — see [§8](#8-what-must-not-be-touched-yet).

**Deliverable:** [`DESIGN_SYSTEM.md`](DESIGN_SYSTEM.md) is now the source of truth
for all UI work. This document records what changed to get there.

**Build state:** `npx tsc --noEmit` exits 0.

---

## 1. What changed

### 1.1 The token layer was restructured, not rewritten

The existing `palette.ts → themes.ts → typography.ts + layout.ts → tokens.ts →
useDesignTokens()` chain was sound and was kept. What changed inside it:

- **Themes no longer carry shape.** The `soft` boolean and the per-theme `surface`
  radius overrides are gone. They were the mechanism producing two competing
  visual languages: the *same card* was `radius: 2` (sharp HUD) on four themes and
  `radius: 22` (rounded) on the others. There is now one radius scale for all
  eight themes.
- **Colour became semantic.** `ThemeColors` now declares the full role set —
  `background`, `surface`, `surfaceElevated`, `surfaceSunken`, `overlay`,
  `text.{primary,secondary,muted,disabled,onPrimary}`,
  `border{,Subtle,Strong,Accent}`, `primary{,Pressed,Subtle}`, `focus`,
  `disabled` — across all eight themes.
- **Status became a palette, not a colour.** `StatusRole = { base, onBase,
  subtle, border }` × `{ success, warning, danger, info, neutral }`, resolved
  light/dark from the active theme. A status can now tint a fill, a border and its
  own label without a component hardcoding anything.
- **Typography became a role system.** A 14-role scale replacing
  `baseTokens.typography`, which had *zero* consumers — every screen hand-wrote
  `fontSize`/`fontFamily`.
- **Layout primitives were formalised**: spacing (4px base), radius (4 steps +
  `full`), elevation (4 steps, border-or-shadow rule), `TOUCH_TARGET_MIN` +
  `control` heights + `hitSlopFor()`, `chrome` dimensions, `zIndex` scale,
  `motion`.

### 1.2 `core/ui/` became the design system

It went from six exports (three live, two orphaned, one used by a single screen)
to **26 components**, covering all 20 roles Phase 2 required plus `Divider`,
`PressableScale`, `ErrorBoundary`, `FieldShell`, `MeshBackground` (deprecated) and
`useContentInsets`.

### 1.3 The layout system took ownership of the screen shell

`PageLayout` + `useContentInsets` now own safe areas, scroll, keyboard insets,
content padding, the header slot and the sticky footer slot. Consequences:

- **Zero screens read `useSafeAreaInsets()`.** The only remaining readers are the
  tab bar (which must own its own inset) and the three overlays rendered outside
  a `PageLayout` tree (`BottomSheet`, `ConfirmDialog`, `Toast`).
- **Every arbitrary bottom padding is gone**: `paddingBottom: 150` (shared
  layout), `paddingBottom: 100` (screens that used it *on top of* the 150),
  `paddingBottom: 84` (a footer), `bottom: 8` (the tab bar, which imported
  `useSafeAreaInsets()` and never applied it), and both
  `<View style={{ height: 100 }} />` spacers.

### 1.4 Raw values were migrated out of the screens

| Migration | Sites |
| --- | --- |
| `isDark ? '#000' : '#FFF'` → `text.onPrimary` | 24 |
| Per-screen scrim alphas (0.6 / 0.7 / 0.8 / 0.92) → `overlay` | 5 |
| Translucent-white fills → `surfaceSunken` / `status.neutral.subtle` | 8 |
| Translucent-white borders → `borderSubtle` / `borderStrong` / `status.neutral.border` | 5 |
| Hardcoded lemberg green → `borderAccent` | 1 |
| Static white switch knob → `onPrimary` / `text.muted` | 1 |
| Hand-rolled logo plate → `background` | 1 |
| Raw brand hexes → `BRAND_COLORS` | 2 |

**Remaining raw colours in `app/` and `src/` outside `src/core/design/`: five, all
commented at the call site.**

1. `app/map.tsx:339` — a map pin's drop shadow. Needs neutral black because it
   falls on arbitrary third-party map tiles, not on a themed surface.
2. `src/components/VoucherDetailModal.tsx:346` — the QR quiet zone. Scanners need
   a true-white margin around the symbol; it must not follow the theme.
3. `src/components/grid-background.tsx:61,73` — the grid wash (~3–4% alpha) in an
   already-deprecated decorative component. `borderSubtle` is an opaque hairline
   meant to be *seen*; at grid density it turns texture into graph paper.
4. `src/core/ui/MeshBackground.tsx:79` — a decorative highlight stop, inside an
   already-deprecated component.

### 1.5 i18n was completed for the migrated surfaces

265 → **272 keys, verified equal across `en`, `uk`, `de`, `es`.** The seven new
keys are the map screen's copy, which was hardcoded English literals inside
`app/map.tsx` in a four-language app. The login screen is now translated rather
than Ukrainian-only, which closes the loop the audit found: a German speaker could
not log in in their own language, and the language selector was behind `/profile`,
which requires login.

---

## 2. Components created

All exported from `src/core/ui/index.ts`. The 20 Phase 2 required roles:

| Component | Role |
| --- | --- |
| `PageLayout` | Screen shell: safe areas, scroll, keyboard, padding, header/footer slots |
| `ScreenHeader` | The one header — back, title, subtitle, up to two actions |
| `Card` | Container primitive — tone, accent stripe, selected, pressed, disabled |
| `Button` | Action primitive — 4 variants × 3 sizes × default/pressed/disabled/loading |
| `IconButton` | Icon-only action; `accessibilityLabel` is a **required** prop |
| `TextField` | Labelled input with helper/error slots; 5 states |
| `Select` | Option picker, opens a themed `BottomSheet` not a platform dialog |
| `BottomSheet` | Bottom-anchored modal panel |
| `ConfirmDialog` | Blocking confirmation, `tone: neutral \| warning \| destructive` |
| `Toast` (`ToastHost` + `toast.*`) | Transient confirmation |
| `InlineFeedback` | A message bound to the thing it is about |
| `SectionHeader` | Groups content within a screen |
| `StatTile` | A labelled number in a summary row |
| `Chip` | Filter / selectable tag |
| `Badge` | Status label |
| `EmptyState` | Nothing here, and that is valid |
| `LoadingState` | Spinner, `fullScreen` or `inline` |
| `ErrorState` | Failure with a retry path |
| `Price` | Monetary amount, formatted and aligned |
| `QuantityStepper` | −/value/+ for litres and quantities |
| `ListItem` | Row in a list or settings group |

Supporting, not in the required 20: `Text` (the type-scale enforcement point),
`Divider`, `PressableScale`, `ErrorBoundary`, `FieldShell`, `useContentInsets`.

**Nothing was created to raise the component count.** The two judgement calls:
`Divider` exists because the alternative is a `View` with a hairline colour in
every screen; `FieldShell` exists because a date-picker trigger and a phone prefix
must match a `TextField` exactly, and without it they were hand-rolled.

---

## 3. Components consolidated

| Was | Now | Detail |
| --- | --- | --- |
| Multiple competing headers | `ScreenHeader` | **11 of 12 screens.** The one exception is `app/index.tsx`, whose LEMBERG banner is brand identity, not screen chrome — deliberately excluded. |
| 7 hand-rolled centred `ActivityIndicator` blocks | `LoadingState` | `/report`, `/my-codes`, `/contracts`, `/company`, `/invitations`, `/profile` (×2). |
| 5 modal scrims, 5 different alphas | `tokens.colors.overlay` | 0.6 / 0.7 / 0.8 / 0.92 → one value that also lightens correctly on the three light themes. |
| Duplicate press components | `PressableScale` | Used *by* `Card` and `ListItem` rather than by screens. |
| Hand-rolled login inputs | `PhoneAuthForm` on `TextField` / `Button` / `InlineFeedback` | Also translated. |
| Per-screen bottom padding arithmetic | `useContentInsets` | One derivation from `chrome` measurements. |
| `LoadingIndicator` (orphaned in `core/ui/`, zero imports) | deleted | Verified unused before deletion. |
| The dead QR implementation | deleted | Verified unused before deletion. |

### The loading-state bug this closed

Six of the seven hand-rolled blocks shared a structural defect worth naming
because it is easy to reintroduce: they `return`ed a bare centred `View`
**instead of** the screen's `PageLayout`. So during loading the screen had no
header, no background and no safe-area insets, then visibly re-assembled itself
when the data arrived. `app/profile.tsx` additionally rendered a hardcoded English
`"SECURITY REDIRECT..."` in a four-language app.

The corrected pattern, now documented in `LoadingState`'s own docblock and in
`DESIGN_SYSTEM.md` §12:

```tsx
if (isLoading) {
  return (
    <PageLayout header={Header} disableScroll>
      <LoadingState fullScreen />
    </PageLayout>
  );
}
```

---

## 4. Tokens created and changed

### Created

| Token | Why |
| --- | --- |
| `colors.surfaceSunken` | Recessed surfaces: input wells, inset QR/code areas, stepper buttons, track backgrounds, spent items. Eight screens had hand-rolled this as a translucent white that only existed on the dark themes. |
| `colors.surfaceElevated` | The floating-surface fill, so `Card tone="elevated"` does not reuse `surface`. |
| `colors.overlay` | One scrim. `isDark ? 'rgba(0,0,0,0.72)' : 'rgba(17,17,20,0.44)'`. |
| `colors.text.onPrimary` | **Derived from the luminance of `primary`, not from `isDark`.** Three themes have a light primary with a dark on-primary (`#001B0A`, `#00231A`, `#04252B`); the old `isDark ? '#FFF' : '#000'` rule produced white-on-`#00FF6A` at ≈2:1 on the default theme. |
| `colors.borderSubtle` / `borderStrong` / `borderAccent` | Three distinct jobs — quiet separator, emphasised outline, brand-tinted selection — previously all raw `rgba(255,255,255,X)`. |
| `colors.text.disabled`, `colors.disabled`, `colors.focus` | So no component writes a disabled or focused style. |
| `colors.status.*` (`StatusRole` × 5) | Status as a 4-value palette per role, light/dark aware. |
| `colors.status.neutral` | **Used / expired / archived / closed.** A spent voucher is not an error, and before Phase 2 it shared the brand colour with an active one. |
| `typeScale` (14 roles) + `fonts` + `MIN_FONT_SIZE` | Replacing an unused `baseTokens.typography`. |
| `spacing` (10 steps + 4 named aliases) | 4px base. |
| `radius` (5 steps + `full`) | Replacing radii of 2, 3, 4, 8, 12, 22, 28 found in production. |
| `elevation` (4 steps) | Encodes the border-or-shadow rule. |
| `TOUCH_TARGET_MIN`, `TOUCH_TARGET_GAP`, `control`, `hitSlopFor()` | So a 44pt target is derivable, not remembered. |
| `chrome` | The measured heights (`tabBarHeight` 56, `headerHeight` 56, `footerHeight` 72, `contentBottomGap` 16) that bottom padding is derived from. |
| `zIndex` (9 steps) | So nothing invents `zIndex: 100`. |
| `motion` | Durations, press scale/opacity, spring config. |
| `surface` | Maps radii onto element kinds: `card`/`button`/`field`/`icon`/`pill`/`accentWidth`. |

### Changed

- **Per-theme `surface` radius overrides: removed.** This is the fix for the two
  shape languages.
- **`surface.soft`: permanently `true`.** Retained only so unmigrated screens
  compile. Every `soft ? … : …` in a screen is now a dead branch — 30 of them in
  7 files. See [§6](#6-remaining-design-system-problems).
- **`fonts.bodyBlack` (Inter 900): retained but unused by any role.** 900 weight
  at 9px was the old micro-label style.
- **Deprecated colour aliases retained** so unmigrated screens compile:
  `text.dim`, `text.neon`, `primaryDim`, `primaryGlow`, `accent`, `card`
  (→`surface`), `borderLight` (→`borderSubtle`), `error`
  (→`status.danger.base`).

### Added during Phase 2's own validation pass

**`Button size="sm"` (36pt).** Step 11 asks whether the major screen types can be
built without inventing new styles. For buttons the answer was *no*: four screens
had hand-rolled a compact inline action (`retryBtn` 12/6 padding, `smallBtn`
10/7, `payButton` 14/7, basket's clear-cart), and `Button` offered only `md` (52)
and `lg` (56). Three of the four were also **below the 44pt minimum** — roughly
28–31pt tall.

The fix uses tokens that already existed and were never exposed: `control.sm`
(36, documented as "compact chips and inline controls — requires `hitSlop`") and
`hitSlopFor()`. `size="sm"` is 36pt to look at and 44pt to hit, and defaults to
`fullWidth: false`, because a compact action that stretches edge to edge is not
compact.

**`aria-invalid` on `TextField`.** The component's docblock claimed the error state
set an invalid flag; the implementation never did. A screen-reader user got the
danger border and the red message via `accessibilityHint` but was never told the
field was *rejected*. `accessibilityState` has no `invalid` member, so
`aria-invalid={!!error}` is the flag. Docblock corrected to match.

---

## 5. Deprecated components

Deprecated **in place** — still live, documented, do not extend. Each carries an
`@deprecated` docblock naming its call sites and why deleting it is Phase 3.

| Component | Live call sites | Replacement | Why still live |
| --- | --- | --- | --- |
| `src/components/page-layout.tsx` | 13 screens | `core/ui/PageLayout` | A documented shim that forwards correctly. Migrating the call sites is mechanical but touches every screen. |
| `GridBackground` | `checkout`, `landing`, `my-codes`, `report`, + the shim's default | No background — `PageLayout` has none by default | Removing it changes the appearance of checkout and the wallet. |
| `MeshBackground` | `FuelCard`, `PackageCard`, `VoucherCard`, `VoucherDetailModal`, `my-codes` | `Card` (`tone`, `accent`) | It is the *fill* of five card components. |
| `GlowText` | `basket`, `index`, `map`, `my-codes`, `FuelCard` | `Text role="title" \| "display"` | Removing it changes the brand banner and two titles. |
| Colour aliases | many | see §4 | They keep unmigrated screens compiling. |
| `fonts.bodyBlack` | unmigrated screens | `fonts.bodyBold` | No role uses it. |

`GridBackground` and `MeshBackground` are deprecated on the merits, not merely for
tidiness: the design direction rules out decorative grids and texture that competes
with content, and both sit behind screens about money, litre counts and signed
contracts. `Card` therefore has **no** gradient or pattern slot — a content surface
is colour plus a hairline, and that is the whole vocabulary.

### Deleted

`LoadingIndicator` (orphaned in `core/ui/`, zero imports) and the dead QR
implementation. Both were verified genuinely unused before deletion, not deleted
for looking redundant. Also removed as provably dead: `app/map.tsx`'s
`styles.header` / `title` / `subtitle` / `locationBtn` / `attribution` /
`attributionText`, its unused `brandName` local, and `styles.center` /
`styles.centerContainer` in the five screens whose loading states migrated.

---

## 6. Remaining design-system problems

Ordered by how much they will cost later.

### Adoption gaps — the foundation exists, the screens are not on it

| # | Problem | Size | Blocked on |
| --- | --- | --- | --- |
| 1 | **`Card` has zero call sites.** Five parallel card implementations remain (`StationCard`, `FuelCard`, `PackageCard`, `VoucherCard`, `OrderCard`, plus an inline card in `my-codes`). | 6 components | Their radii, mesh fills and accent widths all change. It is the wallet and the package list. |
| 2 | **`Button` has four call sites.** Every other action is a raw `Pressable` + `Text` — 24 distinct hand-rolled button styles across 9 files. | ~24 sites | Visual change to every screen. |
| 3 | **13 screens still import the deprecated `page-layout` shim.** | 13 files | Mechanical, but it changes each screen's default background (the shim defaults to `GridBackground`). |
| 4 | **124 `allowFontScaling={false}` occurrences in 13 files.** `core/ui/Text` never disables scaling, so this is entirely unmigrated-screen debt. | 124 sites | Each removal needs its layout checked at 1.6× — that is a redesign of the screen's vertical rhythm. |
| 5 | **18 `Alert.alert` product call sites** (`company` 6, `contracts` 4, `invitations` 2, `my-codes` 3, `profile` 3). | 18 sites | Replacing an alert with `ConfirmDialog` / `toast` / `InlineFeedback` changes flow behaviour, not just appearance. |
| 6 | **30 dead `soft ? … : …` ternaries in 7 files.** `surface.soft` is permanently `true`, so only the true branch ever renders. | 30 sites | Nothing technically — but all 7 files are the card components item 1 will rewrite, so cleaning them now is wasted work. |

### Genuine duplicates still shipping

| # | Problem |
| --- | --- |
| 7 | **Three live voucher representations** (`VoucherCard`, `VoucherBadge`, `VoucherDetailModal`) with overlapping status logic. Consolidating them is the wallet redesign. |
| 8 | **`app/station/[id].tsx`'s `fuelGrid` double-spaces** because `FuelCard` carries its own margin *and* the grid sets a gap. A one-line fix, but it is inside a card component slated for rewrite. |

### Copy and content

| # | Problem |
| --- | --- |
| 9 | **Screen titles are all-uppercase in all four locales** (`'NETWORK MAP'`, `'PROFILE'`, `'YOUR BASKET'`…). This now *conflicts with* `ScreenHeader`'s sentence-case contract: the component renders `title` at the `title` role, and the string arrives shouting. A copy pass across 4 locales is Phase 3. |
| 10 | **`useLogin` dumps its whole `logs` array into the user-facing error string under `__DEV__`.** Not a design-system problem strictly, but it is user-visible text that no design rule governs. |

### Deliberately not done

| # | Decision |
| --- | --- |
| 11 | **Basket's clear-cart still has no destructive confirmation.** `ConfirmDialog tone="destructive"` exists and is documented; wiring it in is a behavioural change to the purchase flow, which Phase 2 was explicitly scoped out of. |
| 12 | **`VoucherDetailModal`'s `QrScannerOverlay` sweep was recoloured but not removed.** Removing the animation is correct per the motion rule (no looping animation) but it changes the redemption screen. |

---

## 7. Visual changes shipped in Phase 2

These are appearance changes a reviewer will notice. None is a flow redesign, but
they are not invisible either, and they should be looked at on device before this
merges.

### Headers

| # | Change |
| --- | --- |
| a | **All nine migrated headers moved from centred/oversized titles (32 / 28 / 24pt) to `ScreenHeader`'s left-aligned `title` role (26pt).** This is the single most visible change in Phase 2. |
| b | **`my-codes` (the wallet) lost its pulsing `GlowText` title.** |
| c | **`checkout` lost its centred 32pt title and its 8pt / 4-letter-spacing subtitle.** |
| d | **`packages` lost its brand-coloured `GlowText` fuel name.** |
| j | **`map` lost its glowing centred title and gained the standard left-aligned header. Its vertical position also shifted**, because it was the only screen reading `useSafeAreaInsets()` inside a `PageLayout` that already applies the top inset — it was adding the notch twice. The hairline under the header was kept: the map is the one edge-to-edge canvas in the app, so its title needs a boundary that whitespace gives the other screens for free. |

### Navigation and chrome

| # | Change |
| --- | --- |
| e | **`basket`'s back button changed from `router.push('/')` to `router.back()`.** A bug fix — the old one grew the navigation stack on every "back". |
| f | **The app-lock screen lost its rotated diamond wordmark, its glow, and `GridBackground`.** |
| g | **The update wall lost `GridBackground` and now uses Rajdhani instead of the OS system font.** |

### Colour and surface

| # | Change |
| --- | --- |
| k | **Five modal scrims changed opacity** (0.6 / 0.7 / 0.8 / 0.92 → one `overlay` token). Most visible on the QR detail modal, which **lightened** from 0.92. |
| l | **The wallet's "USED" watermark** changed from a dark plaque with 25%-white text to the neutral status role (subtle tint, 50–55% text). **Used-voucher rows** now use `surfaceSunken`, which is slightly *darker* than the card on dark themes — the old raw value was slightly *lighter*. |
| m | **`PackageCard`'s litre box and stepper buttons** changed from a lighter-than-card wash to `surfaceSunken`. |
| n | **The profile legal-entity switch knob is `text.muted` when off**, not white. The old static `#FFF` was ≈1.1:1 against the light themes' `#EDECE7` off-track — the switch looked empty. |
| o | **The home screen's logo reticle ring is `borderAccent`** (theme-tinted) instead of always lemberg green. |
| h | **`VoucherDetailModal`'s `QrScannerOverlay` sweep was recoloured**, deliberately not removed (see §6.12). |

### Auth

| # | Change |
| --- | --- |
| i | **`app/landing.tsx`'s sign-in UI is now `PhoneAuthForm`** — shared `TextField` / `Button` / `InlineFeedback` instead of hand-rolled inputs — **and it is translated** rather than Ukrainian-only. |

### Loading

| # | Change |
| --- | --- |
| — | **Six screens no longer lose their header, background and safe areas while loading** (§3). The visible effect is that the screen stops re-assembling itself when data arrives. `profile`'s `"SECURITY REDIRECT..."` string is gone; it was untranslated internal jargon on a view the user passes through in milliseconds, so it was dropped rather than given an i18n key. |

---

## 8. What must not be touched yet

Phase 2's constraint, restated because §6 makes it tempting:

> Do **not** redesign checkout. Do **not** redesign the wallet. Do **not** redesign
> the QR redemption flow. Do **not** redesign navigation. Do **not** redesign the
> purchase flow.

Concretely, leave alone until Phase 3 is briefed:

- `app/checkout.tsx`, `app/basket.tsx` — the purchase flow.
- `app/my-codes.tsx` and the three voucher components — the wallet.
- `src/components/VoucherDetailModal.tsx` — the QR redemption screen. Its raw
  `#FFFFFF` quiet zone in particular is **not** a violation to fix; scanners need
  it.
- `src/components/bottom-tabs.tsx` — navigation. Its direct `useSafeAreaInsets()`
  read is correct: the tab bar must own its own inset.
- The five card components (`StationCard`, `FuelCard`, `PackageCard`,
  `VoucherCard`, `OrderCard`) — migrating them onto `Card` *is* the Phase 3 visual
  redesign, and doing it piecemeal would leave the app with six card looks instead
  of two.
- `GridBackground` / `MeshBackground` / `GlowText` — deprecated, not dead.
  Deleting any of them changes checkout, the wallet or the brand banner.

---

## 9. Technical risks

| Risk | Assessment |
| --- | --- |
| **Deprecated colour aliases mask unmigrated code.** `card`, `borderLight`, `error`, `text.dim` still resolve, so a screen using them compiles and looks *almost* right. | Accepted deliberately — removing them would have required migrating all 13 shim consumers in the same change. Mitigation: they are documented as forbidden in new code, and the alias list is short enough to grep. |
| **`tsconfig.json` sets `noUnusedLocals: false` and `noUnusedParameters: false`.** A plain `npx tsc --noEmit` will **not** catch a dead import. | Audit with `npx tsc --noEmit --pretty false --noUnusedLocals` when removing code. Doing so is how the dead `styles.center` / `ActivityIndicator` imports in this change were confirmed removed. |
| **`t()` falls back to the raw key.** So `t('a.b') \|\| 'fallback'` is *always* truthy and the fallback never fires; a typo ships as `codes.availablePayloads` rendered literally. | Documented in `DESIGN_SYSTEM.md` §13. There is no compile-time key check — adding one is worth considering in Phase 3. |
| **`disableScroll` is now load-bearing.** `LoadingState fullScreen` is `flex: 1`, and two screens (`contracts`, `profile`) were nesting a `ScrollView` inside `PageLayout`'s own scroll view. Both now pass `disableScroll`, and `profile`'s inner `ScrollView` gained `flex: 1`. | Verify on device that `contracts` and `profile` scroll to their last item. This is the change in Phase 2 most likely to have a layout regression, because the magic 100pt spacers that were papering over it are gone. |
| **`Button size="sm"` is new and has no call sites yet.** | It is validated by type-check only. The first screen to adopt it should confirm the 36pt height with 4pt `hitSlop` behaves on both platforms. |
| **Font scaling is now on everywhere `core/ui/Text` is used.** Screens not yet migrated still pin it off, so the app currently scales *inconsistently*. | This is strictly better than uniformly-broken, but it means QA at 1.6× will show two behaviours until item 4 in §6 is done. |
| **Eight themes × every component is a large test surface.** Contrast was reasoned about via the token layer (`text.onPrimary` derived from luminance, status palettes resolved per light/dark) rather than measured on all eight. | Spot-check `glass` (translucent surfaces, where shadows have nothing to fall on) and `white`/`sorbet`/`blade` (the three light themes, where the old raw translucent-white values were invisible). |

---

## 10. Validation — Step 11

| Question | Answer |
| --- | --- |
| Can all major screens use the shared header? | **Yes — 11 of 12.** `app/index.tsx` is excluded deliberately: its LEMBERG banner is brand identity, not screen chrome. |
| Can cards use the shared `Card`? | **Yes, with one deliberate omission.** Every real card's needs map onto `tone` / `accent` / `selected` / `padding`. The exception is the decorative mesh fill in four of them, which `Card` intentionally cannot express — see §5. Zero call sites yet; that is adoption, not capability. |
| Can actions use the shared `Button`? | **Now yes.** It was *no* until this pass: compact inline actions had no size. Fixed by adding `size="sm"` (§4). Icon-only actions map to `IconButton`, steppers to `QuantityStepper`. The one shape not reproducible is `contracts`' dashed-border button, which is ad-hoc decoration rather than a needed variant — `variant="secondary"` covers it. |
| Can feedback use the new infrastructure? | **Yes for capability; partially adopted.** All seven mechanisms exist and the selection rule is documented (§12 of the design system). 18 `Alert.alert` sites remain because replacing them changes flow behaviour. |
| Can status colours work across every theme? | **Yes.** `StatusRole` × 5 roles, light/dark resolved from the theme. No component hardcodes a status colour. The `neutral` role specifically fixed spent vouchers sharing the brand colour with active ones. |
| Can layouts handle safe areas consistently? | **Yes.** Zero screens read insets; all bottom padding is derived from measured `chrome`; every magic number is gone. |
| Can typography scale appropriately? | **Foundation yes, screens no.** `core/ui/Text` never disables scaling and caps per role. 124 `allowFontScaling={false}` sites remain in unmigrated screens — screen debt, not a foundation defect. |
| Can the major screen types be built without inventing new styles? | **Yes, after two fixes made during this validation.** The two gaps found were real and both are now closed: `Button size="sm"` and `TextField`'s missing `aria-invalid`. Everything else the screens had hand-rolled wanted a role that already existed — `surfaceSunken` for wells, `overlay` for scrims, `status.neutral` for spent items, `borderAccent` for selection. **The screens had not been migrated; the tokens were not missing.** |

---

## 11. Next

Phase 3 is not started. The natural first move, given §6, is the item everything
else is blocked behind: **migrate the five card components onto `Card`**, because
it unblocks items 1, 4, 6 and 7 simultaneously and it is where the app's remaining
visual inconsistency actually lives.

Awaiting instruction.
