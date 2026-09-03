# FuelFlow Design System

**Status:** established in Phase 2. This document is the source of truth for all
future UI work in this repository.

If you are about to write a `fontSize`, a `borderRadius`, a hex colour or a
`paddingBottom` inside a screen, stop and read the relevant section. Almost
always the answer is that a token or a component already exists. If one genuinely
does not, extend `src/core/ui/` — do not add the style to the screen.

**Source of truth in code:**

| Layer | File |
| --- | --- |
| Raw palette | [`src/core/design/palette.ts`](../../mobile/src/core/design/palette.ts) |
| Themes (8) + semantic colour roles | [`src/core/design/themes.ts`](../../mobile/src/core/design/themes.ts) |
| Type scale + font families | [`src/core/design/typography.ts`](../../mobile/src/core/design/typography.ts) |
| Spacing, radius, elevation, touch targets, chrome, z-index, motion | [`src/core/design/layout.ts`](../../mobile/src/core/design/layout.ts) |
| Assembled token object | [`src/core/design/tokens.ts`](../../mobile/src/core/design/tokens.ts) |
| Hook screens consume | `useDesignTokens()` in [`src/core/hooks/useTheme.ts`](../../mobile/src/core/hooks/useTheme.ts) |
| Components | [`src/core/ui/`](../../mobile/src/core/ui/) (import from the barrel, `src/core/ui/index.ts`) |

---

## 1. Design principles

The product sells prepaid fuel vouchers to drivers and manages fuel contracts for
companies. Users are standing at a pump, in a moving vehicle, or approving a legal
document — often in bright sunlight, often in a hurry, often one-handed. The
design language follows from that, not from what looks current.

1. **Clarity before character.** If a choice makes the screen more distinctive but
   less immediately legible, it loses. A driver reading a litre count at a pump is
   the benchmark user.
2. **Hierarchy is created by size and weight, not by decoration.** The most
   important thing on a screen is the biggest thing on it. The audit found the
   opposite: the *smallest* elements (8–9px uppercase labels with 8px letter
   spacing) carried the meaning while the largest carried glow effects.
3. **Numbers are the content.** Prices, litres, balances and codes are what the
   product is about. They get the display face, the largest sizes, and consistent
   alignment. Everything else is supporting text.
4. **One way to do each thing.** One header, one card, one button, one loading
   state, one scrim. A second implementation of an existing concept is a bug.
5. **State is part of the component.** A component that does not define its
   pressed, disabled, loading and error appearances is unfinished. Screens must
   never hand-roll a disabled style.
6. **Confidence in money moments.** Anything that spends money, redeems a voucher
   or signs a document states plainly what will happen, shows the amount, and is
   reversible or confirmed. It never relies on the user inferring state.
7. **No decoration that competes with the task.** Explicitly out of the system:
   glow, looping animation, decorative grids, hexagonal mesh texture behind
   numbers, gradients used as differentiation, HUD/reticle framing, letter spacing
   as a style. These are not "not preferred" — they are removed or deprecated.
8. **Accessibility is a floor, not a feature.** 44pt minimum targets, font scaling
   on, contrast checked against every theme, `accessibilityRole` on every
   interactive element.

### What was deliberately rejected

| Rejected | Why |
| --- | --- |
| The sharp `radius: 2` "HUD/console" language | Reads as a technical instrument panel. Wrong for money and signed contracts. |
| Full pills for containers | Playful, and it wastes the horizontal space numeric alignment needs. |
| Neon glow as an emphasis device | Illegible in sunlight, meaningless as hierarchy, and it does not survive the five non-green themes. |
| Uppercase micro-labels as the dominant device | Puts the meaning in the least readable element on the screen. |
| A generic "modern app" look | The domain determines the language. See principle 1. |

---

## 2. Visual language

### One shape philosophy

**Moderate, consistent, hierarchical.** Radius encodes *how far a surface is from
the page*, so it decreases as you nest inward:

```
sheet / dialog (20)  >  card / tile / panel (14)  >  control (10)  >  badge / chip (6)
```

A child's radius is always smaller than its parent's. `radius.full` (999) is for
things that are genuinely circular — status dots, avatars, round icon-only
affordances — never for a rectangle.

Before Phase 2 the app carried **two competing shape languages** simultaneously:
themes declared a `soft` boolean and per-theme `surface` radius overrides, so the
same card was `radius: 2` on four themes and `radius: 22` on the others. Both the
flag's effect and the overrides are gone; **themes no longer carry shape.** A
theme changes colour and nothing else.

### One differentiation rule

**A surface uses a border OR a shadow, never both.**

- **Content surfaces** (cards, rows, tiles) — background colour + 1px hairline
  border. No shadow. This keeps dense lists calm, and it is the only thing that
  works on the translucent `glass` theme, where a shadow has nothing to fall on.
- **Floating surfaces** (sheets, dialogs, toasts, the tab bar) — shadow, and they
  drop the border. They must read as detached from the content beneath.

The audit found borders, shadows and glows applied simultaneously, plus
`shadowRadius: 10` glow blobs used as an active-state indicator.

### Surface stack

| Role | Used for |
| --- | --- |
| `background` | The screen canvas. |
| `surfaceSunken` | Recessed: input wells, inset code/QR areas, stepper buttons, track backgrounds, spent items. |
| `surface` | The default content card or row. |
| `surfaceElevated` | A card that floats (`Card tone="elevated"`), sheets, dialogs. |
| `overlay` | The scrim behind any sheet, dialog or full-screen overlay. |

### Motion

Animation does exactly two jobs: **acknowledge a press** and **explain where a
surface came from**. There are no looping animations in this system. (The audit
found a 2px red laser line looping permanently across a scannable QR code.)

| Token | Value | Use |
| --- | --- | --- |
| `motion.fast` | 120ms | Press feedback, state cross-fade |
| `motion.normal` | 220ms | Sheet / dialog entry and exit |
| `motion.slow` | 320ms | Full-screen transition |
| `motion.pressScale` | 0.985 | Press scale on a card or row |
| `motion.pressOpacity` | 0.7 | Press opacity where a control must not resize |

---

## 3. Colour semantics

Colour is consumed **only** through semantic roles. There are eight themes
(`lemberg`, `white`, `blue`, `obsidian`, `nova`, `glass`, `sorbet`, `blade`) —
five dark, three light — and a component that reaches for a raw hex is broken on
at least five of them.

### Surface and text roles

| Role | Meaning |
| --- | --- |
| `background` | Screen canvas |
| `surface` | Default content surface |
| `surfaceElevated` | Floating surface (shadowed) |
| `surfaceSunken` | Recessed surface (wells, tracks, spent items) |
| `overlay` | Scrim behind sheets, dialogs, full-screen overlays |
| `text.primary` | The content |
| `text.secondary` | Supporting copy |
| `text.muted` | Metadata, inactive states |
| `text.disabled` | Disabled control labels |
| `text.onPrimary` | Content **on top of** `primary` — see the warning below |
| `border` | Default hairline |
| `borderSubtle` | Quieter separator inside a surface |
| `borderStrong` | Emphasised outline, active track |
| `borderAccent` | Brand-tinted outline (selection, focus ring, secondary button) |
| `primary` | The brand / primary action fill |
| `primaryPressed` | Pressed state of a `primary` fill |
| `primarySubtle` | Faint brand wash (selected card, ghost press) |
| `focus` | Focus ring |
| `disabled` | Disabled control fill |
| `isDark` | Boolean, for the rare case a component needs to know |

> **`text.onPrimary` is derived from the luminance of `primary`, not from
> `isDark`.** Three themes have a light `primary` with a *dark* on-primary
> (`#001B0A`, `#00231A`, `#04252B`). The pattern `isDark ? '#FFF' : '#000'`
> appeared at 24 call sites and produced white-on-`#00FF6A` (≈2:1 contrast) on
> the default theme. Never write that ternary. Use `tokens.colors.text.onPrimary`.

### Status roles

Status is a **palette of four values per role**, not a single colour, so a status
can tint a fill, a border and its own label:

```ts
type StatusRole = { base; onBase; subtle; border }
tokens.colors.status.{ success | warning | danger | info | neutral }
```

Light and dark variants are resolved automatically from the active theme. Status
colours therefore work across all eight themes, and **no component may hardcode
one**.

| Role | Use |
| --- | --- |
| `success` | Completed, paid, signed, active |
| `warning` | Expiring, needs attention, partial |
| `danger` | Failed, blocked, destructive action |
| `info` | Neutral information, hints |
| `neutral` | **Used, expired, archived, closed.** A spent voucher is not an error. Before Phase 2 it shared the brand colour with an active one. |

### Rules

1. **No raw colour in `app/` or `src/` outside `src/core/design/`.** Enforced by
   review. The four surviving exceptions are each documented at their call site:
   the map pin drop shadow (needs neutral black over arbitrary map tiles), the QR
   quiet zone (scanners need true white), and the two deprecated decorative
   layers (`GridBackground`, `MeshBackground`).
2. **Never `isDark ? … : …` for contrast.** That is what `text.onPrimary` and the
   status roles are for. Eight themes, not two.
3. **Never a per-screen scrim alpha.** One `overlay` token. Five screens
   previously picked their own (0.6 / 0.7 / 0.8 / 0.92), and none of them
   lightened on the light themes.
4. **Brand colours** (station brand identity — OKKO, WOG, UPG, KLO) come from
   `BRAND_COLORS`, not from the theme, because they are third-party marks.
5. Deprecated colour aliases (`text.dim`, `text.neon`, `primaryDim`,
   `primaryGlow`, `accent`, `card`, `borderLight`, `error`) exist **only** so
   unmigrated screens compile. Do not use them in new code. Their replacements:
   `text.muted` / `text.secondary`, `primary`, *(none — glow is out)*, `primary`,
   `surface`, `borderSubtle`, `status.danger.base`.

---

## 4. Typography

Two faces, deliberately divided:

- **Rajdhani** (condensed display) — titles and numbers. It is wrong for prose.
- **Inter** — all prose, labels and control text.

| Role | Font | Size / line | Tracking | Transform | Scale cap | Use |
| --- | --- | --- | --- | --- | --- | --- |
| `display` | Rajdhani Bold | 34 / 38 | −0.6 | — | 1.25 | One per screen: a hero value or landing statement |
| `title` | Rajdhani Bold | 26 / 30 | −0.4 | — | 1.30 | Screen title — the "where am I" line |
| `heading` | Rajdhani Bold | 20 / 24 | −0.2 | — | 1.35 | Card title, dialog title, in-content heading |
| `sectionTitle` | Inter Bold | 13 / 16 | 0.3 | UPPER | 1.30 | Groups content within a screen |
| `body` | Inter | 15 / 22 | 0 | — | 1.60 | Default reading size |
| `bodyStrong` | Inter Bold | 15 / 22 | 0 | — | 1.60 | A name, a selected option, an answer |
| `secondary` | Inter | 13 / 18 | 0 | — | 1.50 | Descriptions, addresses, helper text |
| `caption` | Inter | 12 / 16 | 0 | — | 1.40 | Dates, counts, footnotes — the smallest role |
| `label` | Inter Medium | 12 / 16 | 0.4 | UPPER | 1.30 | Field labels, badge text |
| `numericLarge` | Rajdhani Bold | 32 / 36 | −0.6 | — | 1.25 | A total or balance — the number the screen is about |
| `numeric` | Rajdhani Bold | 22 / 26 | −0.2 | — | 1.30 | A price or quantity in a card or row |
| `numericSmall` | Inter Bold | 14 / 18 | 0 | — | 1.40 | An inline amount inside a sentence |
| `button` | Inter Bold | 15 / 20 | 0.2 | — | 1.30 | Default control label |
| `buttonSmall` | Inter Bold | 13 / 16 | 0.2 | — | 1.30 | Compact control label |

### Rules

1. **Nothing smaller than 12px.** 8px and 9px text existed in 14 files.
2. **Letter spacing never exceeds 0.4.** The previous scale went to 8.
3. **Uppercase is a role, not a default.** Exactly two roles are uppercase
   (`sectionTitle`, `label`). Everything else is sentence case — including screen
   titles.
4. **Font scaling stays on.** `core/ui/Text` never disables it; each role caps
   itself with `maxFontSizeMultiplier`. Controls cap at 1.3 because they live in
   fixed-height touch targets; prose caps at 1.6. Do not add
   `allowFontScaling={false}`.
5. Screens use `<Text role=… tone=… />` from `core/ui`, never RN `Text` with a
   hand-written `fontSize`. `core/ui/Text` props are `role`, `tone`, `center`
   (there is no `align`), plus the standard RN text props.

---

## 5. Spacing

A 4px base unit. `tokens.spacing`:

| Token | px | Use |
| --- | --- | --- |
| `xxs` | 2 | Hairline gaps, icon-to-glyph nudges |
| `xs` | 4 | Tightest real gap |
| `sm` | 8 | Inside a chip; icon to its label |
| `md` | 12 | Inside a compact row |
| `lg` | 16 | **Default gap between siblings; default card padding** |
| `xl` | 20 | Generous card padding |
| `2xl` | 24 | **Screen horizontal padding** |
| `3xl` | 32 | Between content groups |
| `4xl` | 40 | Between major sections |
| `5xl` | 56 | Above a terminal element (empty state, success) |

Named aliases, kept because they are widely referenced and they say *why*:
`containerPadding` (24), `cardGap` (16), `sectionGap` (32), `hairline`
(`StyleSheet.hairlineWidth`).

**No screen sets its own vertical rhythm at the page level.** `PageLayout` applies
`gap: spacing.lg` between children and owns all horizontal and bottom padding.

---

## 6. Radii

`tokens.radius`:

| Token | px | Use |
| --- | --- | --- |
| `none` | 0 | Full-bleed edges |
| `sm` | 6 | Badges, chips, tags, inline code |
| `md` | 10 | Buttons, text fields, icon buttons, list rows, segmented controls |
| `lg` | 14 | Cards, tiles, panels |
| `xl` | 20 | Bottom sheets, dialogs, full-bleed modals |
| `full` | 999 | Circles only — dots, avatars, round icon buttons |

Radii the audit found in production and which no longer exist as choices: 2, 3, 4,
8, 12, 22, 28.

`tokens.surface` maps these onto element kinds so components do not have to
decide: `card` → `lg`, `button` → `md`, `field` → `md`, `icon` → `md`, `pill` →
`full`, `accentWidth` → 3.

> `tokens.surface.soft` is permanently `true`. It survives only so unmigrated
> screens compile; every `soft ? … : …` ternary in a screen is a dead branch.
> Do not write new ones.

---

## 7. Elevation

`tokens.elevation`:

| Token | Use |
| --- | --- |
| `none` / `flat` | Flush with the canvas. Content surfaces — the border does the work. |
| `low` | Menus, popovers, the tab bar. y+2, blur 8, 12% |
| `medium` | Bottom sheets, toasts. y+6, blur 16, 18% |
| `high` | Dialogs — the only thing above a sheet. y+12, blur 28, 24% |

Rules: **border or shadow, never both** (§2). Shadow means "this floats above the
content"; if the surface does not float, it does not get a shadow. Glow is not an
elevation level and is not part of the system.

`tokens.zIndex` exists so nothing has to invent `zIndex: 100`: `background 0 <
content 1 < sticky 10 < header 20 < footer 30 < tabBar 40 < sheet 50 < dialog 60
< toast 70`.

---

## 8. Component inventory

Import everything from the barrel: `import { … } from '@/core/ui'`. Screens should
import from `core/ui` and nowhere deeper.

### Layout

| Component | Purpose | Key props |
| --- | --- | --- |
| `PageLayout` | The screen shell. Owns safe areas, scroll, keyboard insets, content padding, the header slot and the sticky footer slot. | `header`, `footer`, `scroll`, `refreshControl`, `padding`, `hasTabBar`, `background` |
| `useContentInsets` | The derived bottom inset, for a screen that owns its own scroll container. | `{ hasFooter?, hasTabBar? }` |
| `ScreenHeader` | The one header. Back affordance, title, optional subtitle, up to two trailing actions. | `title`, `subtitle`, `onBack`, `hideBack`, `actions`, `compact` |
| `SectionHeader` | Groups content within a screen. | `title`, `subtitle`, `action`, `icon` |
| `Divider` | A hairline separator. | `inset`, `tone`, `vertical` |

### Typography

| Component | Purpose | Key props |
| --- | --- | --- |
| `Text` | All text. Resolves role → type style and tone → colour. | `role`, `tone`, `center` |

`tone`: `primary | secondary | muted | disabled | accent | onAccent | success |
warning | danger | info | inherit`.

### Controls

| Component | Purpose | Key props |
| --- | --- | --- |
| `Button` | The action primitive. | `label`, `variant`, `size`, `disabled`, `loading`, `icon`, `fullWidth`, `emphasis` |
| `IconButton` | Icon-only action. `accessibilityLabel` is **required**. | `icon`, `accessibilityLabel`, `variant`, `size`, `round` |
| `Chip` | A filter or a selectable tag. | `label`, `selected`, `icon`, `count` |
| `QuantityStepper` | −/value/+ for litres and quantities. | `value`, `onChange`, `min`, `max`, `step`, `unit`, `onRemove`, `size` |
| `PressableScale` | Press feedback primitive, used *by* components. Screens should rarely need it directly. | `scaleTo`, `hapticFeedback`, `pressedStyle` |

`Button.variant`: `primary` (the one action the screen wants — at most one per
view) · `secondary` (a real alternative, outlined) · `ghost` (low-stakes, no fill
or border) · `destructive` (outlined in danger; filled only via
`emphasis="high"` inside a `ConfirmDialog`).

`Button.size`: `lg` 56pt (screen primary) · `md` 52pt (secondary, in-form) · `sm`
36pt (a compact action *inside* another element — a card header, a list row, a
retry beside an error line; keeps a 44pt target via `hitSlop` and defaults to
`fullWidth: false`).

`IconButton.variant`: `plain | outlined | filled | danger`. `size`: `sm | md | lg`.

### Inputs

| Component | Purpose | Key props |
| --- | --- | --- |
| `TextField` | Labelled text input with helper and error slots. | `label`, `helper`, `error`, `required`, `leading`, `trailing`, `codeStyle` |
| `FieldShell` | The field chrome without an input, for a custom control that must match a field (a date picker trigger, a phone prefix). | `label`, `helper`, `error`, `onPress`, `trailing` |
| `Select` | Option picker. Opens a `BottomSheet` rather than a native dialog. | `options`, `value`, `onChange`, `label`, `placeholder`, `sheetTitle` |

### Surfaces

| Component | Purpose | Key props |
| --- | --- | --- |
| `Card` | The container primitive. | `onPress`, `selected`, `accent`, `tone`, `padding` |
| `ListItem` | A row in a list or settings group. | `title`, `subtitle`, `leading`, `trailing`, `showChevron`, `selected`, `destructive`, `divider` |
| `BottomSheet` | A modal panel anchored to the bottom edge. | `visible`, `onClose`, `title`, `footer`, `scrollable`, `maxHeightRatio` |

`Card.tone`: `default` (surface + hairline) · `sunken` · `elevated` (shadow, drops
the border). `Card.accent` puts a 3px status stripe on the leading edge — use it
for objects whose state matters at a glance (an unpaid order, a spent voucher).
Selection is a **border** change plus a faint wash, not a fill change, so a
selected card in a list of five still reads as the same kind of object.

### Data display

| Component | Purpose | Key props |
| --- | --- | --- |
| `Badge` | A status label. | `label`, `status` (`success \| warning \| danger \| info \| neutral \| primary`) |
| `Price` | A monetary amount, formatted and aligned. | `amount`, `size`, `tone`, `original`, `signed`, `decimals`, `align` |
| `StatTile` | A labelled number in a summary row. | `label`, `value`, `caption`, `icon`, `accent`, `onPress` |

**Never format currency by hand.** `Price` wraps
[`src/core/utils/currency.ts`](../../mobile/src/core/utils/currency.ts) —
`formatMoney`, `splitMoney`, `formatLitres`, `formatPercent`. That module uses a
narrow no-break space as the group separator, a comma decimal and a true minus
sign (U+2212), and deliberately avoids `Intl` (unreliable across RN/Hermes
builds).

### Feedback

| Component | Purpose | Key props |
| --- | --- | --- |
| `InlineFeedback` | A message bound to the thing it is about. | `kind`, `message`, `title`, `action`, `onDismiss` |
| `ConfirmDialog` | A blocking confirmation. | `visible`, `title`, `message`, `confirmLabel`, `tone`, `loading` |
| `ToastHost` / `toast` | Transient confirmation. `toast.success/warning/danger/info(…)` | mounted once in `app/_layout.tsx` |
| `EmptyState` | There is nothing here *and that is a valid state*. | `title`, `description`, `icon`, `action`, `secondaryAction`, `compact` |
| `LoadingState` | A spinner with an optional label. | `message`, `fullScreen`, `variant`, `size` |
| `ErrorState` | Something failed and the user can retry. | `title`, `description`, `onRetry`, `detail`, `variant`, `fullScreen` |
| `ErrorBoundary` | Catches a render crash and shows `ErrorState` instead of a white screen. | wraps the app in `_layout` |

---

## 9. Component states

A component owns its states. A screen must never write a disabled style, a
pressed style, or an error border.

### Button

| State | Appearance |
| --- | --- |
| default | Variant fill or outline |
| pressed | Background steps to `primaryPressed` (fills) or `primarySubtle` (outlines). **No scale** — a 56pt bar scaling reads as jitter. |
| disabled | `colors.disabled` fill, `text.disabled` label, no border tint, press and haptics suppressed |
| loading | Spinner replaces the label, **width held** so the button does not resize, press suppressed, `accessibilityState.busy` set |

`loading` implies non-interactive. Screens no longer need their own
`isProcessing` guard around `onPress` — a stuck `isProcessing` flag was how
checkout's CTA became permanently dead after a payment hand-off failure.

### TextField

| State | Appearance |
| --- | --- |
| default | `surfaceSunken` well, 1pt `border` hairline, label in `label` role |
| focused | Border steps to `focus` at 1.5pt; the label does not move (no floating-label animation) |
| filled | Same as default — the value itself signals the state |
| error | Border `status.danger.base` at 1.5pt, message below in `status.danger.base` with an alert icon, `aria-invalid` set |
| disabled | `colors.disabled` fill, `text.disabled` value, not editable |

The error message replaces the helper text in the same slot, so the field never
changes height when validation fires. Presence of the `error` prop *is* the error
state — a field is never "in error" without saying why.

### Card / ListItem

| State | Appearance |
| --- | --- |
| default | `surface` + hairline |
| pressed | `motion.pressScale` (0.985) + `primarySubtle` tint, via `PressableScale` |
| selected | `borderAccent` border + `primarySubtle` wash. A border change, not a fill change. |
| disabled | Opacity 0.5, press suppressed |

### Feedback kinds

`success` · `warning` · `danger` · `info` — each resolves to a `StatusRole`
(`base` for the icon and text, `subtle` for the fill, `border` for the outline).
There is no fifth kind, and no component invents its own.

---

## 10. Touch-target rules

1. **Minimum 44 × 44pt on both platforms.** `tokens.touchTarget.min`.
2. A control that is visually smaller **must** extend its target with
   `hitSlop` — use `tokens.touchTarget.slopFor(height)`, which computes the pad
   needed to reach 44.
3. Two adjacent independent targets need at least `tokens.touchTarget.gap`
   (8pt) between them.
4. Control heights come from `tokens.control`: `sm` 36 (compact, requires
   `hitSlop`) · `md` 44 (icon buttons, secondary actions — meets the minimum
   exactly) · `lg` 52 (text fields, selects, list rows, default buttons) · `xl`
   56 (the primary action on a screen).
5. Icon-only controls use `IconButton`, which enforces the target and **requires**
   `accessibilityLabel`.

The audit found a 40×40 icon-only contract-signing button, a 32×32 modal close,
and a `paddingVertical: 7` "PAY" button around 9px text (≈31pt tall).

---

## 11. Safe-area rules

**The layout system owns safe areas. Screens do not.**

1. **Do not call `useSafeAreaInsets()` in a screen.** `PageLayout` wraps every
   screen in `SafeAreaView edges={['top', 'left', 'right']}`. Reading insets
   again inside a screen adds the notch a second time — which is exactly what the
   map screen did.
2. The only legitimate direct readers of insets are components rendered **outside**
   a `PageLayout` tree: the tab bar itself (`src/components/bottom-tabs.tsx`) and
   the three overlays (`BottomSheet`, `ConfirmDialog`, `Toast`).
3. **Bottom padding is derived, never typed.** `useContentInsets` returns
   `insets.bottom + (hasTabBar ? chrome.tabBarHeight : 0) + (hasFooter ?
   chrome.footerHeight : 0) + chrome.contentBottomGap`. Do not add anything to
   the returned value. If content is still clipped, the chrome measurement in
   `layout.ts` is wrong and should be corrected **there**.
4. **A sticky footer belongs in `PageLayout`'s `footer` slot**, which owns its own
   `paddingBottom: insets.bottom + tabBarHeight + spacing.lg` and its hairline
   top border. A screen must not build its own absolutely-positioned action bar.
5. Eliminated by these rules and forbidden in new code: `paddingBottom: 150` in
   the shared layout, `paddingBottom: 100` in the screens that used it,
   `paddingBottom: 84` in a footer, a hardcoded `bottom: 8` on the tab bar with
   `useSafeAreaInsets()` imported and never applied, and `<View style={{ height:
   100 }} />` spacers at the end of a scroll view.

### If a screen needs its own `ScrollView`

Pass `disableScroll` (shim) / `scroll={false}` (core) so there is exactly one
scroll container, and give the inner `ScrollView` `flex: 1`:

```tsx
<PageLayout header={Header} disableScroll>
  <ScrollView style={{ flex: 1 }} contentContainerStyle={{ paddingBottom: contentInsets.bottom }}>
    …
  </ScrollView>
</PageLayout>
```

Nesting a `ScrollView` inside `PageLayout`'s own scroll view is a bug: the
derived bottom padding never reaches the inner scroller, and the usual
compensation is a magic 100pt spacer.

---

## 12. Feedback rules

Choose the mechanism by **importance and persistence**, not by convenience. Not
everything is a toast.

| Situation | Mechanism | Why |
| --- | --- | --- |
| A field is invalid | `TextField error` | The message belongs beside the thing that is wrong. |
| A form or a section failed | `InlineFeedback kind="danger"` | Persistent, in place, and does not steal focus. |
| A whole screen failed to load | `ErrorState` with `onRetry` | The user needs a way forward, not an apology. |
| There is nothing here, legitimately | `EmptyState` with an action | An empty list is a state, not an error. |
| Loading a screen for the first time | `LoadingState fullScreen` **inside** `PageLayout` | See below. |
| Refreshing content already on screen | `LoadingState variant="inline"` or `refreshControl` | Never wipe visible content to a spinner. |
| An action succeeded and the result is visible anyway | Nothing | Silence is valid feedback. |
| An action succeeded and the result is *not* visible | `toast.success(…)` | Transient, non-blocking. |
| An action is irreversible or spends money | `ConfirmDialog` | Blocking, states the consequence, names the action in the button. |
| A destructive action | `ConfirmDialog tone="destructive"` + `Button variant="destructive" emphasis="high"` | The confirming button carries the danger, not the trigger. |

### Rules

1. **`LoadingState fullScreen` goes inside the layout, never instead of it.**

   ```tsx
   if (isLoading) {
     return (
       <PageLayout header={Header} disableScroll>
         <LoadingState fullScreen />
       </PageLayout>
     );
   }
   ```

   Six screens previously `return`ed a bare centred `View`, so during loading the
   screen had no header, no background and no safe-area insets, then visibly
   re-assembled itself when data arrived. `disableScroll` matters: `fullScreen`
   is `flex: 1` and needs a fixed-height parent to centre in.

2. **Do not use `fullScreen` for a filter change.** `/report` set `loading = true`
   on every period tap, wiping the screen to a centred spinner and destroying the
   user's scroll position and sense of place.

3. **`Alert.alert` is not the feedback system.** It cannot be themed, cannot be
   translated consistently, blocks the JS thread, and looks like an OS error. Use
   the primitives above. (18 product call sites remain — see
   `PHASE_2_IMPLEMENTATION.md`.)

4. **Confirmation copy names the action.** "Delete voucher", not "OK". The user
   should be able to read only the buttons and still know what happens.

---

## 13. Accessibility rules

1. **Font scaling stays on.** Never `allowFontScaling={false}`. Roles cap
   themselves via `maxFontSizeMultiplier` (1.3 for controls in fixed-height
   targets, up to 1.6 for prose). If a layout breaks at 1.6, the layout is wrong,
   not the setting.
2. **Nothing below 12px.** `MIN_FONT_SIZE` in `typography.ts`.
3. **44pt minimum targets**, `hitSlop` where the visual size is smaller (§10).
4. **`accessibilityLabel` is required on every icon-only control.** `IconButton`
   makes it a required prop so this cannot be forgotten.
5. **Roles on interactive and structural elements**: `accessibilityRole="button"`
   on pressables, `"header"` on the screen title (`ScreenHeader` does this),
   `"progressbar"` with `accessibilityState.busy` on loading
   (`LoadingState` does this), `accessibilityState.{disabled, busy, selected}`
   wherever those states exist.
6. **Never signal state by colour alone.** A status always carries a label or an
   icon as well as a tint — that is why `Badge` takes a `label` and `StatusRole`
   has a `base` for text as well as a `subtle` for fill.
7. **Contrast is checked against all eight themes**, not against the default one.
   `text.onPrimary` exists because a two-theme assumption produced a ≈2:1 pair.
8. **All user-visible copy goes through `t()`.** Four locales (`en`, `uk`, `de`,
   `es`), 272 keys, kept equal.

> **`t()` gotcha:** it falls back to returning the raw key, so
> `t('a.b') || 'fallback'` is *always* truthy and the fallback never fires. If a
> key might be missing, add the key.

---

## 14. Usage rules

**For screens:**

1. Import from `@/core/ui` and nowhere deeper.
2. Every screen is `<PageLayout header={<ScreenHeader … />}>`. One header
   component, one layout.
3. No `fontSize`, `fontFamily`, `borderRadius`, `padding` number, colour literal,
   or `zIndex` in a screen. Use `Text role=`, `tokens.radius`, `tokens.spacing`,
   `tokens.colors`, `tokens.zIndex`.
4. No `useSafeAreaInsets()`. No arbitrary bottom padding. No spacer views.
5. No currency formatting by hand — `Price` or `formatMoney`.
6. No `Alert.alert` for product feedback.
7. `onBack` defaults to `router.back()`. **Never pass `router.push` as a back
   action** — `/basket` used `router.push('/')`, which grew the navigation stack
   on every "back".
8. Screen titles are **sentence case**. `ScreenHeader` renders `title` at the
   `title` role, not as an uppercase label.

**For components:**

1. A component that needs a style no token provides is a gap in the token layer.
   Add the token; do not inline the value.
2. Define all four state groups (default / pressed / disabled / error-or-loading)
   before considering a component done.
3. If you are about to write a second implementation of something in §8, don't —
   extend the existing one.

**If the system genuinely lacks something**, extend `src/core/ui/` and document
it here in the same commit. `Button size="sm"` was added during Phase 2's own
validation pass, precisely because four screens had hand-rolled a compact inline
action at 28–31pt.

---

## 15. Deprecated patterns

### Deprecated in place — still live, do not extend

| Thing | Live call sites | Replacement | Why not deleted yet |
| --- | --- | --- | --- |
| `src/components/page-layout.tsx` (shim) | 13 screens | `core/ui/PageLayout` | The shim forwards correctly; migrating the call sites is mechanical but touches every screen. |
| `GridBackground` | `checkout`, `landing`, `my-codes`, `report`, + the shim default | No background (`PageLayout` has no default) | Removing it changes the look of checkout and the wallet — Phase 3. |
| `MeshBackground` | `FuelCard`, `PackageCard`, `VoucherCard`, `VoucherDetailModal`, `my-codes` | `Card` (`tone`, `accent`) | Same — it is the fill of five card components. |
| `GlowText` | 5 sites | `Text role="title" / "display"` | Removing it changes the brand banner and two titles. |
| Deprecated colour aliases | many | see §3 rule 5 | They keep unmigrated screens compiling. |
| `tokens.surface.soft` | 30 dead ternaries in 7 files | nothing — it is permanently `true` | Those seven files are the card components Phase 3 will rewrite. |
| `fonts.bodyBlack` (Inter 900) | unmigrated screens | `fonts.bodyBold` | No role uses it; 900 weight at 9px was the old label style. |

### Forbidden outright

| Pattern | Instead |
| --- | --- |
| `isDark ? '#000' : '#FFF'` for contrast | `tokens.colors.text.onPrimary` |
| A per-screen scrim alpha (`rgba(0,0,0,0.8)`) | `tokens.colors.overlay` |
| Raw status colours | `tokens.colors.status.*` |
| A raw brand hex | `BRAND_COLORS` |
| A hand-rolled header | `ScreenHeader` |
| A hand-rolled centred `ActivityIndicator` | `LoadingState` inside `PageLayout` |
| A hand-rolled card (`surface` + border + radius by hand) | `Card` |
| A hand-rolled `Pressable` + `Text` button | `Button` / `IconButton` |
| `useSafeAreaInsets()` in a screen | `PageLayout` / `useContentInsets` |
| `paddingBottom: 84 / 100 / 120 / 150`, `<View style={{ height: 100 }} />` | derived insets (§11) |
| `allowFontScaling={false}` | nothing — leave scaling on (§13) |
| `bottom: 8` on fixed chrome | the safe-area inset |
| Font sizes below 12 | `caption` (12) is the floor |
| Letter spacing above 0.4 | the type scale |
| Glow (`shadowRadius` on text, `primaryGlow`) | size and weight |
| Looping animation | nothing; motion is press + entry only (§2) |
| Uppercase screen titles | sentence case (§4 rule 3) |
| `Alert.alert` for product feedback | §12 |
| Currency formatting by hand | `Price` / `formatMoney` |
| `t('key') \|\| 'fallback'` | add the key (§13) |

---

## Related documents

- [`PHASE_2_IMPLEMENTATION.md`](PHASE_2_IMPLEMENTATION.md) — what changed in
  Phase 2, what is deliberately untouched, and the remaining backlog.
- [`DESIGN_PROBLEMS.md`](DESIGN_PROBLEMS.md) — the audit's problem catalogue, with
  resolution status.
- [`UX_AUDIT.md`](UX_AUDIT.md) — the 81 Phase 1 findings.
- [`APP_INVENTORY.md`](APP_INVENTORY.md), [`USER_FLOWS.md`](USER_FLOWS.md),
  [`INFORMATION_ARCHITECTURE.md`](INFORMATION_ARCHITECTURE.md).
