# UX / UI AUDIT — FuelFlow Mobile

> Phase 1 findings. Every item below was observed by reading the actual source.
> No code was modified. No fixes are proposed here — remedies belong to Phase 2.
>
> **Severity**
> - **P0** — fundamental UX problems: the user can lose money, lose data, be
>   blocked from the core task, or be unable to complete the job the app exists for.
> - **P1** — major UX/UI problems: the flow works but is confusing, wrong-shaped,
>   or actively degrades trust.
> - **P2** — quality problems: inconsistent, generic, or unfinished.
> - **P3** — polish.

**Counts:** P0 = 12 · P1 = 24 · P2 = 31 · P3 = 14

---

# P0 — Fundamental

### P0-1 · The cart is cleared before the payment is confirmed
`app/checkout.tsx`

The order is created, the cart is emptied, and *then* the user is sent out to the
Monobank hosted invoice. If the user cancels on the payment page, backgrounds the
app, loses signal, or the bank declines the card, they return to an **empty
cart** and an unpaid order. There is no recovery path in the UI: the packages
must be re-selected from scratch, at whatever the prices are now.

Compounding it: `isProcessing` is not reset when the hand-off fails, so the
checkout CTA stays in its disabled/spinner state. The user's only escape is to
kill the app.

This is the single most damaging defect in the product. It occurs at the exact
moment the user is trying to give the business money.

### P0-2 · A fully refunded order disappears from the app
`app/my-codes.tsx:187-188`, `core/types/api.ts`

`Order.status` supports `'REFUNDED'`. The wallet filters into two buckets:
`PENDING_*`, and `FULFILLED || PARTIALLY_REFUNDED`. `REFUNDED` matches neither,
so the order renders in **no section at all**. There is no refunds screen, no
refund state on any card, and no notification.

From the user's point of view money left their account, an order existed, and now
the order does not exist. The one tile that still counts it —
`SummaryBar` uses unfiltered `orders.length` — produces a visible contradiction:
"5 ORDERS" printed above four cards.

### P0-3 · An animated red laser is drawn across the customer's own QR code
`src/components/VoucherDetailModal.tsx:15-54`

```tsx
const QrScannerOverlay = () => { ...
  <Animated.View style={[{ position:'absolute', top:12, left:12, right:12,
    height:2, backgroundColor:'#DC2626', zIndex:10, opacity:0.8 },
    { transform:[{ translateY: scanAnim }] }]} /> }
```

A 2px `#DC2626` bar loops permanently over the QR. This is sci-fi decoration
applied to the one surface in the entire product that must be **machine
readable**, at the one moment the user cannot troubleshoot (standing at a pump,
holding the phone out to a scanner). A moving high-contrast line across a QR
degrades decode reliability and, if it fails, the user is stranded at a petrol
station with fuel they have already paid for.

Decoration is not neutral here. It is directly opposed to function.

### P0-4 · Decrementing quantity to zero silently deletes the cart line
`src/features/cart/store/cartStore.ts`

```ts
updateQuantity: (itemId, quantity) => set((state) => {
  if (quantity <= 0) { return { cart: state.cart.filter((c) => c.id !== itemId) }; }
```

`CartItemCard` calls `onUpdateQuantity(item.id, (item.quantity ?? 1) - 1)`
unguarded, so tapping "−" on a quantity of 1 destroys the item. No confirmation,
no undo, no toast. The minus button does not visually change to a delete button,
and it is not disabled at 1. A user tapping "−" to adjust and then "+" to go back
finds the row gone.

### P0-5 · The most legally significant action in the app is an unlabelled 40×40 circle
`app/contracts.tsx`

Signing a B2B fuel supply contract is triggered by a **40×40 circular icon-only
button** containing a `PenTool` glyph. No label, no caption.

The review surface is capped at `sheetScroll: { maxHeight: 180 }` — roughly six
lines. Reading the actual contract requires opening a *second* `Modal` on top of
the signing `Modal`, behind a dashed-border secondary button (`readFullBtn`) that
reads as tertiary. There is no "I have read and agree" checkbox, no scroll gate,
and no record shown of what was signed.

A user can legally bind a company by tapping an unlabelled circle next to six
lines of text they were never required to scroll.

### P0-6 · Declining a worker invitation is irreversible and unconfirmed
`app/invitations.tsx`

Accept → `Alert` confirmation afterwards. Decline → nothing but a haptic. The
invitation is destroyed server-side. The user has no way to know whether the tap
registered, and no way to undo it. The two buttons sit adjacent, at the same
size, with the same weight.

The destructive action is the one with *less* feedback than the safe one.

### P0-7 · Conditional hook order — a real crash path in the wallet
`app/my-codes.tsx:169-190`

```tsx
const [expandedOrders, setExpandedOrders] = useState(...);        // 169
if (!isAuthenticated && !authLoading) return <Redirect href="/landing" />;  // 171
const assignedVoucherIds = useMemo(..., [...]);                   // 190
```

The `useMemo` at 190 sits **after** an early `return`. When the user is
unauthenticated the hook count changes between renders → React
"Rendered fewer hooks than expected" on the very next render. This is reachable
by any session/token expiry while `/my-codes` is mounted, i.e. exactly when the
user opens their wallet at a pump.

### P0-8 · Production users are shown raw cryptographic diagnostics
`src/components/phone-auth.tsx`

```tsx
diagSummary = ` [SIG=${diagData.method} rsa=${diagData.rsaPkcs1Valid} kt=${diagData.keyType} ks=${diagData.rsaKeySize} fp=${diagData.keyFingerprint} clientValid=${diagData.valid}]`;
throw new Error(`${err.error?.message || 'Помилка верифікації пристрою'}${diagSummary}`);
```

This is the **production** branch, not `__DEV__`. A login failure presents the
end user with key type, RSA key size and a key fingerprint appended to the error
message. It is simultaneously incomprehensible, alarming, and an information
leak. It appears at the app's first-impression moment.

(The `__DEV__` counterpart in `useLogin.ts` dumps a multi-line step log into the
same error slot via `setError(logs.join('\n'))`.)

### P0-9 · Discount logic is client-side and shipped in the bundle
`src/features/cart/types/index.ts:16`

```ts
export const PROMO_CODES: Record<string, number> = { FUEL10: 10, SAVE15: 15, POWER20: 20, LEMBERG25: 25 };
```

Anyone who opens the bundle has every promocode. This is a commercial problem,
but it is also a UX problem: because validation is local, the promo field can
never express the states real promocodes have — expired, already used,
minimum-spend not met, not valid for this brand. The UI has exactly two states,
accepted and red border.

### P0-10 · The login screen is hardcoded Ukrainian in a 4-language app
`src/components/phone-auth.tsx`

20 hardcoded Cyrillic strings (`ВХІД ЗА ТЕЛЕФОНОМ`, `ПІДТВЕРДЖЕННЯ`, `БЕЗПЕКА`,
`УСПІШНО`, `ВВЕДІТЬ КОРЕКТНИЙ НОМЕР`). `PhoneAuthForm` adds 12 more. The app
ships complete uk/en/de/es translations — 235 keys, in sync — and then bypasses
them on the one screen every single user must pass through.

The language selector lives in `/profile`, which is unreachable until you have
logged in. A non-Ukrainian speaker cannot reach the setting that would let them
read the screen that is blocking them.

### P0-11 · The security-setup step is a dead end
`src/components/phone-auth.tsx` / `PhoneAuthForm.tsx`

The `security_setup` step has no cancel, no back, and no skip. If device key
generation fails — a common condition on emulators, rooted devices, and some OEM
keystores — the user is parked on a terminal screen with no exit but force-quit,
and force-quitting returns them to the same place.

### P0-12 · Android users cannot enter a birthdate
`app/profile.tsx`

The Android branch of the birthdate picker is never rendered. The field exists,
is presented as editable, and cannot be filled on Android. If any downstream
flow requires it, Android users are silently blocked.

---

# P1 — Major

## Purchase funnel

### P1-1 · Five competing primary CTAs and no primary action
`src/features/stations/components/PackageCard.tsx` (409 LOC)

Each `PackageCard` is a full ~350px configuration form: litre box, price,
savings badge, `QUANTITY` micro-label, stepper, `TOTAL` divider, and a
`height: 64` filled CTA, all at `padding: 24`. A station offering five packages
therefore presents **five identical maximum-emphasis CTAs stacked vertically**.

Nothing is recommended, nothing is default, nothing is de-emphasised. Every
option shouts equally, which is the same as none of them shouting. The user must
compare five forms instead of choosing between five products.

### P1-2 · Adding to the cart leaves the user nowhere
`PackageCard.tsx`

On success `isAdded` locks the card. There is no route to the cart, no toast, no
count change in view (the tab bar is hidden on `/packages`), and no
"continue / checkout" decision offered. The user's action produces a disabled
card and silence. They must discover the way out themselves.

### P1-3 · Fuel selection inverts its own hierarchy
`src/features/stations/components/FuelCard.tsx`

On the screen whose only job is "pick a fuel", the **fuel name** renders at 16px
in `text.dim`, while the **price** renders at 28px in the brand colour. The
primary decision attribute is the least visible thing on the card.

### P1-4 · The same gesture tilts by three different amounts on three consecutive screens
`StationCard` `outputRange: ['0deg','3deg']` · `FuelCard` `'8deg'` · `PackageCard` `'5deg'`

One press gesture, one funnel, three physics. `8deg` in particular reads as a
glitch rather than feedback. Nothing in the product distinguishes these three
cards such that they should respond differently to being touched.

### P1-5 · Bottom chrome flickers in and out mid-purchase
`src/components/bottom-tabs.tsx:26`

Hidden on `/station/`, `/packages`, `/checkout`, `/payment-result` — but **shown**
on `/basket`, which sits between `/packages` and `/checkout`. The funnel is
therefore tabs → none → tabs → none. Either the funnel is a focused modal flow or
it is not; it is currently both.

### P1-6 · Login is demanded at checkout, after the work is done
`app/checkout.tsx:15`

Authentication is deferred to the last step, and it is a *different*
implementation (`PhoneAuthForm`) from the one on `/landing` (`PhoneAuth`) — a
different-looking OTP screen appears at the moment of highest purchase intent.
Combined with P0-1 (cart cleared on hand-off), the failure surface at the
money-moment is unusually large.

### P1-7 · Stale prices are persisted indefinitely
`cartStore.ts`

The whole store is persisted, including `selectedStation`, `selectedFuel`,
`selectedPackage`. A cart built weeks ago rehydrates with its old prices and
feeds `/packages`. There is no freshness check, no "prices have changed" state,
and no expiry.

## Wallet & redemption

### P1-8 · The redemption rules the user needs are fetched and thrown away
`getVouchers.ts:33-34`, `core/types/api.ts`

`redemptionRules` and `fuelSubtype` are mapped into the client model and never
rendered anywhere. The information that tells the customer *how* to redeem at the
pump reaches the device and stops there.

### P1-9 · Order status lives entirely outside the theme
`src/components/OrderCard.tsx`

```tsx
accentColor = needsPayment ? '#EF4444' : (isPending ? '#F59E0B'
  : (isPartiallyRefunded ? '#a855f7' : '#22c55e'))
```
plus `rgba(34,197,94,…)` / `rgba(245,158,11,…)` literals. These are default
Tailwind hues dropped into a custom-branded app. On the `lemberg` theme the
"fulfilled" green `#22c55e` sits next to the brand green `#16FF00` and reads as a
rendering error.

### P1-10 · Redemption state is not encoded in colour
`src/components/VoucherCard.tsx`

`getStatusConfig` maps **both** `used` and `active` to `brandColor`. The one
distinction that matters in a voucher wallet — spent vs unspent — is carried only
by a text label and `opacity`, and the used state's `rgba(255,255,255,0.02)`
background is invisible or inverted on the light themes.

### P1-11 · The accordion clips its own content
`OrderCard.tsx`

```tsx
const vouchersContentHeight = useMemo(() => voucherCount > 0 ? voucherCount * 260 : 56, [voucherCount]);
```

A magic 260px per voucher, interpolated as `maxHeight` with
`useNativeDriver: false`. Any voucher taller than 260px is cut off with no
scroll and no indication that content exists below. It also animates layout on
the JS thread, so it stutters on the exact screen with the most content.

### P1-12 · The PAY button is nested inside the expand toggle
`OrderCard.tsx`

The `PAY` CTA is a child of the header `Pressable`, so tapping it also toggles
the accordion — the card visibly expands as the user leaves for the payment
sheet. It is simultaneously the **smallest** target in the card: 9px text, 10px
icon, `paddingVertical: 7`.

The single revenue-recovering action in the wallet is the hardest thing to hit
and has a side effect.

### P1-13 · Voucher long-press is a no-op that shadows real behaviour
`app/my-codes.tsx`

`onPress` and `onLongPress` both do `setSelectedVoucher(voucher)`; long-press
merely adds a haptic. So the app claims a long-press affordance, teaches nothing
by it, and blocks the platform's own long-press behaviours.

### P1-14 · Manual "mark as used" competes with real redemption
`VoucherDetailModal.tsx`

The user can toggle a voucher to "used" and back. This is self-reported state
sitting next to genuine server-side redemption, with no visual distinction
between the two. A user who taps it to "keep track" now has a wallet whose status
column means two different things depending on the row.

### P1-15 · Two refresh affordances, one of them a raw glyph
`app/my-codes.tsx`

A `RefreshControl` pull-to-refresh **and** a hardcoded `'⟳ REFRESH'` text
button. The glyph is a Unicode character standing in for an icon, in an app that
already depends on `lucide-react-native`.

## Cross-app

### P1-16 · `Alert.alert` is the app's feedback system
~every screen

Because no toast or inline-feedback primitive exists (`core/ui/` has `Button`,
`MeshBackground`, `ErrorBoundary` and two orphans), the OS dialog carries success,
failure, validation and gating. Consequences: feedback is modal and blocking
where it should be ambient; it looks like the OS, not the product; and because
each call site decides independently, some actions confirm loudly while
destructive ones (P0-4, P0-6) confirm not at all.

### P1-17 · Access control implemented as a side effect
`app/contracts.tsx`

```tsx
useEffect(() => { getLegalProfile().then((company) => {
  if (!company) { Alert.alert(t('contracts.needProfile'), ...); router.replace('/profile'); }
}) }, []);
```

The user taps "Contracts", the screen mounts, then an alert fires and they are
thrown to `/profile`. The prerequisite is discovered by being ejected. Nothing on
the entry point indicated a legal profile was required.

### P1-18 · A global busy flag dims every row
`app/invitations.tsx`

```tsx
const isBusy = acceptMutation.isPending || declineMutation.isPending;
```

Applied to all cards. Acting on one invitation greys out all of them, so the
user cannot tell which one is actually in flight.

### P1-19 · Two loading, error and refresh conventions
react-query (`invitations`, `company`) vs manual `useState`/`useEffect`
(`my-codes`, `report`, `contracts`).

`report.tsx` shows a bare centred `ActivityIndicator` on a full-screen wipe for
every period-filter change (`useEffect(..., [period])` → `setLoading(true)`), so
switching from "All time" to "This month" blanks the entire screen instead of
updating in place.

### P1-20 · The map and the list are two destinations, not two views
`index.tsx` / `map.tsx`

Same dataset, separate tabs, no shared state. A user who filters by brand on Home
and then opens the map loses the filter. Selecting a station on the map does not
inform the list. Everywhere else in the category these are a toggle on one
screen.

### P1-21 · `allowFontScaling={false}` app-wide
`core/design/tokens.ts` + every `<Text>`

The app opts out of OS text scaling globally, then builds its type on 8–12px
uppercase labels with `letterSpacing: 1–6` inside **fixed-height** cards
(`104`, `118`, `64`) with `numberOfLines={1}`. Users who enlarge system text —
including anyone who needs to — get no change at all. This is an accessibility
floor, not a preference.

### P1-22 · Touch targets below the platform minimum on critical controls
- `VoucherDetailModal` close: **32×32**
- `CartItemCard` trash: `padding: 4` → ~**28px**
- `OrderCard` PAY: `paddingVertical: 7` around 9px text
- `contracts` back: bare `padding: 8` around a 24px chevron

44pt (iOS) / 48dp (Android) is the floor. The dismiss control on the fullscreen
QR — used one-handed, at a pump, possibly in the rain — is 32px.

### P1-23 · A single button disagrees with itself about contrast
`PhoneAuthForm.tsx:29` vs `core/ui/Button.tsx:42`

```tsx
// icon colour
const filledFg = tokens.colors.isDark ? '#000' : '#FFF';
// label colour, inverted
const filledFg = tokens.colors.isDark ? '#FFF' : '#000';
```

On a filled button that contains both an icon and a label, one of the two is
guaranteed to be invisible against the fill, in every theme.

### P1-24 · The better implementations are the dead ones
`QrFullscreenModal.tsx` (276 LOC, never imported)

It generates the QR locally as SVG (works offline), sizes it to
`SCREEN_WIDTH * 0.78`, uses `borderRadius: 16`, and has no laser overlay. The
shipped `VoucherDetailModal` instead loads a **remote 220×220 `Image`** with a
hardcoded-`#666` "QR Unavailable" fallback and **no retry** — so a network blip
at the pump means no QR at all.

Same for `LoadingIndicator` (orphaned while screens use raw `ActivityIndicator`)
and `PressableScale` (imported by `FuelCard` but never rendered).

The team built the right thing and shipped the wrong thing. Three times.

---

# P2 — Quality

## Visual system

### P2-1 · Six different header implementations
`ScreenHeader` exists and is used by exactly one screen (`station/[id].tsx:38`).
The rest hand-roll. Three files (`contracts`, plus two others) independently
reproduce the same anti-pattern: a 24px title, a bare `padding: 8` chevron, and a
phantom `<View style={{width:24}}/>` spacer for optical centring. Back buttons
are variously 44×44 bordered (`report`), `padding: 8` unbordered (`contracts`),
and `padding: tokens.spacing.sm` bordered with a conditional radius (`basket`).

Title sizes across screens: 32 / 28 / 24 / 18. No scale.

### P2-2 · Five stat-tile designs
`report.tsx` `summaryCard` (`width: '48%'`, `borderRadius: 2`, centred, 22px
Rajdhani value + 8px Inter-Black label) · `my-codes` `SummaryBar`
(`rgba(255,255,255,0.05)` tiles, `fontWeight: '800'` with **no fontFamily**) ·
plus three more in `company`, `profile` and `index`. Same semantic object — a
number with a caption — five renderings.

### P2-3 · Four chip/segment designs
`report.tsx` `filterChip` (`borderRadius: 8`, 9px Inter-Black, `letterSpacing: 1`)
· `index.tsx` brand filters · `my-codes` section pills · `company` role tags.
None share a component.

### P2-4 · Eleven border radii
`2, 4, 6, 8, 10, 12, 16, 18, 20, 22, 28` plus `undefined`. `contracts.tsx` uses
five of them in one file. Almost none read from `tokens.surface`.

Worse, sibling screens contradict each other: `contracts.tsx` cards are
`borderRadius: 2` (sharp) while `invitations.tsx` cards are `12` and its buttons
`10` (rounded). Two B2B screens, one tap apart, in opposite shape languages.

### P2-5 · Core funnel screens render in the OS system font
`basket.tsx` — the entire StyleSheet sets `fontWeight` with no `fontFamily`
(`headerTitle`, `headerSubtitle`, `removeText`, `promoInput`, `applyButtonText`,
`activePromoCode`, `summaryLabel`, `summaryValue`, `totalLabel`,
`emptyStateTitle`). Same in `CartItemCard` (`cardTitle`, `cardBadge`, `itemMeta`,
`itemTotal`) and `my-codes`'s `SummaryBar`.

So the cart — a screen in the paid conversion path — renders in San Francisco /
Roboto while the screen before and after it render in Rajdhani. `CartItemCard`'s
`stepperValue` mixes `fontWeight: '900'` with `fontFamily: 'Rajdhani-Bold'` in
one style, which on iOS silently discards the weight.

### P2-6 · `Inter-Medium` is used but never loaded
Referenced in `my-codes.tsx:395`, `VoucherDetailModal.tsx:123` and elsewhere.
Not in the font load list. Silently falls back to the system font — an invisible
third typeface.

### P2-7 · Dark-only surfaces on an app with light themes
`rgba(255,255,255,0.02)` (used voucher cards), `0.05` (SummaryBar tiles, modal
status pill, disabled action button), `0.08` (tile borders), `0.1` (button
borders). All invisible or inverted on the three light themes. The theme system
exists; these bypass it.

### P2-8 · Status colours outside all 8 themes
`VoucherBadge.tsx`: `gifted_to_me: '#22c55e'`, `gifted_to_worker: '#a855f7'`,
`company_pool: '#3b82f6'` — default Tailwind, hardcoded, at 9px `Inter-Black`.
`bottom-tabs.tsx` badge: `#EF4444`. `OrderCard`: four more (P1-9).

### P2-9 · Currency formatted four different ways
`formatCurrency()` exists in `core/utils/formatters.ts` and is **never called**.
Instead:
- `{pkg.price} ₴` — raw, unrounded (`PackageCard.tsx:134`)
- `{(pkg.price * quantity).toFixed(2)} ₴` — same card, line 237
- `{total} ₴` / `{discountedTotal} ₴` / `-{discountAmount} ₴` — `basket.tsx`, all
  unrounded floats straight from `getDiscountedTotal()`'s
  `total * (1 - discount / 100)`
- `amount.toLocaleString(locale, { style:'currency', currency:'UAH' })` —
  `report.tsx`

15 raw `₴` interpolations app-wide, 6 without `.toFixed(2)`. A 15% discount on
133 ₴ renders as `113.05000000000001 ₴`. In the total field. On the checkout path.

### P2-10 · Section headers differ within a single screen
`my-codes.tsx` — three section headers, three different arrangements of the
icon / rule / label composition (icon-left + rule + label-right for pending and
fulfilled; rule-left + label-right and no icon for unassigned).

### P2-11 · Collapsible sections use Unicode triangles as chevrons
`report.tsx:338, 372` — `{showPayments ? '▲' : '▼'}` at `fontSize: 12`, in an app
that imports `lucide-react-native` on the same screen.

### P2-12 · `letterSpacing: 4` on 12px body-adjacent labels
`report.tsx` `sectionTitle`: `fontSize: 12, letterSpacing: 4`. Elsewhere 8px
labels carry `letterSpacing: 1.5`. Tracking is applied by taste per call site,
not by scale, so it reads as texture rather than hierarchy.

### P2-13 · Two absolute-fill implementations on one element
`page-layout.tsx:37` — `className="absolute inset-0 z-0"` *and* an inline
`{ position:'absolute', top:0, left:0, right:0, bottom:0 }`. A fair sample of how
NativeWind, `StyleSheet` and inline styles are used interchangeably, often within
one component.

### P2-14 · Magic-number bottom padding stacked three deep
`PageLayout` adds a hardcoded `paddingBottom: 150` to every scroll body. Screens
then add their own — `basket.tsx:154` `paddingBottom: 100` on the content **and**
`footer: { paddingBottom: 84 }`; `report.tsx:215` `paddingBottom: 100`. The
floating tab bar is `height: 64` at `bottom: 8`.

Nobody owns the bottom inset (`PageLayout` declares
`edges={['top','left','right']}`), so everyone guesses. Net result: ~250px of
dead space above the fold on some screens, and content under the tab bar on any
screen using `disableScroll` (which skips the 150 entirely).

### P2-15 · The empty state is the best-designed thing in the app, and it is one-off
`basket.tsx:134-150` — 80px icon, 28px title, supporting line, single clear CTA,
generous spacing. It is genuinely good. It is also bespoke to this one screen; no
other empty state resembles it, and there is no `EmptyState` primitive. Where
other screens have empties they are a centred line of `text.dim`.

### P2-16 · Consumer product, military vocabulary
Not a leak — the translation files themselves contain it: `codes.noAssets` →
"NO ASSETS", `codes.availablePayloads` → "AVAILABLE PAYLOADS", `_layout`'s
"SECURING DATA STREAM". Plus a permanent scanning laser (P0-3) and a
`GridBackground` HUD grid behind every screen.

Someone buying 40 litres of diesel is not managing assets or payloads. The
vocabulary and the ornament are describing a different product than the one the
navigation describes.

### P2-17 · Two `isAuthenticated` sources OR'd together
`bottom-tabs.tsx:18-20`

```tsx
const storeAuth = useStore(state => state.isAuthenticated);
const { isAuthenticated: hookAuth } = useAuth();
const isAuthenticated = storeAuth || hookAuth;
```

Two authorities for one boolean, reconciled with `||` at a call site. Whichever
is stale wins whenever it is the truthy one.

### P2-18 · The primary checkout CTA is `variant="secondary"`
`basket.tsx:123-130` — the button that starts payment is rendered in the tinted
outlined variant, while `basket`'s own empty-state "continue shopping" button is
the filled primary. Button hierarchy is inverted between the two states of the
same screen.

### P2-19 · Destructive "remove all" is a bare text link with no confirmation
`basket.tsx:59-61` — `clearCart()` behind 10px uppercase red text in the header,
no dialog, no undo. Meanwhile applying a promocode gets a full bordered button.

### P2-20 · Icon-only bottom tabs with no labels
`bottom-tabs.tsx` — five icons, no text, and the active state is a `primaryDim`
glow blob behind the icon (`activeGlow`) rather than a conventional
colour+label treatment. `QrCode` for "my codes" and `Home` for "stations" are
both guesses the user has to make.

### P2-21 · The floating tab bar ignores the safe area
`bottom-tabs.tsx:46` — `bottom: 8`, hardcoded. `useSafeAreaInsets()` is imported
and destructured but the inset is never applied. On devices with a home
indicator the bar sits in the gesture zone.

### P2-22 · `zIndex` collisions between overlay layers
`bottom-tabs` `zIndex: 100`; `PageLayout` header 40, footer 50;
`QrFullscreenModal` (dead) `zIndex: 100`. Layer order is negotiated with
overlapping magic numbers rather than a stacking scale.

### P2-23 · Order numbers are truncated hashes
`OrderCard.tsx` — `ID: {(order.id || '').slice(0,10).toUpperCase()}`. Meanwhile
`truncateId()` exists in `formatters.ts` and is never called. The user-facing
reference for a payment is ten characters of a UUID.

### P2-24 · Unlabelled dates
`OrderCard` prints `formatExpirationDate(order.createdAt)` with no label —
a creation date passed through a function named for expiry.
`invitations.tsx` prints `formatExpirationDate(inv.createdAtUtc)` bare. The user
sees a date and must guess what it means.

### P2-25 · Mixed i18n inside a single expression
`VoucherDetailModal.tsx:130`

```tsx
{isBlocked ? t('voucher.badge.blocked') : isUsed ? 'REDEEMED' : 'READY'}
```

One ternary, one translated branch, two hardcoded. `OrderCard`'s `statusLabel`
does the same: `t('codes.unpaid') || 'UNPAID'` beside a raw `'REFUNDED'`.

### P2-26 · `VoucherCard` imports i18n and translates one string
`VoucherCard.tsx` — `useI18n` is imported; only `t('codes.expires')` uses it.
`getStatusConfig` returns hardcoded English `'Ready'/'Redeemed'/'Pending'/
'Blocked'/'Expired'`.

### P2-27 · `fontWeight` + `fontFamily` conflicts
`VoucherDetailModal.tsx:360-367` — `modalIdText` sets `fontFamily: 'Inter'` and
`fontWeight: '700'`. On iOS the family wins and the weight is discarded; on
Android behaviour differs. Same pattern in `CartItemCard`'s `stepperValue`.

### P2-28 · OTP entry is one text field with letter-spacing
Both auth implementations. `letterSpacing: 10`, `textAlign: 'center'`,
placeholder `"000000"`. No per-digit boxes, no
`textContentType="oneTimeCode"`, no `autoComplete="sms-otp"` — so iOS and
Android **cannot autofill the code they just received**, and the user retypes it
by hand. There is also no resend timer and no resend affordance.

### P2-29 · No `View` wrapper discipline — 22px + 5px + 16px paddings
`my-codes.tsx:384` — `padding: 22, paddingLeft: 22 + 5 + 16`;
`VoucherDetailModal.tsx:263-267` — `padding: 24, paddingLeft: 24 + 8 + 12,
paddingRight: 12 + 32`. Arithmetic in style values, compensating for absolutely
positioned accent bars and close buttons. Two cards, two different arithmetic
schemes for the same visual idea.

### P2-30 · Diagonal "USED" stamp only in one of the two places a voucher appears
`my-codes.tsx:446-454` renders `diagonalStamp` over used vouchers in the
unassigned list. `VoucherCard` (inside orders) instead uses opacity, and
`VoucherDetailModal` uses a `BlurView`. Three treatments for one state.

### P2-31 · A brand-coloured 5px bar, an 8px bar, and none
`my-codes` unassigned card: 5px left accent. `VoucherDetailModal`: 8px
(`modalAccent`). `OrderCard`: no bar, colour on the border instead. Same
semantic (brand/status accent), three widths including zero.

---

# P3 — Polish

- **P3-1** `Dimensions.get('window')` captured at module scope in `bottom-tabs.tsx:12` and `QrFullscreenModal` — wrong after rotation or split-screen; `width` in `bottom-tabs` is never used at all.
- **P3-2** `QRCode.create(value, { errorCorrectionLevel: 'L' })` in `QrFullscreenModal` — lowest error correction on a code that will be scanned off a scratched screen, and it re-runs on every render building ~1000 `<Rect>` nodes.
- **P3-3** `-{savingsPerUnit} ₴` in `PackageCard` — unrounded.
- **P3-4** `setSigningContract(available.length === 1 ? available[0] : null)` in `contracts.tsx` — auto-selects when there is exactly one contract, so the sheet's behaviour changes silently with data volume.
- **P3-5** `Animated.spring(..., useNativeDriver: false)` on `OrderCard`'s height animation — JS-thread layout animation on the heaviest screen.
- **P3-6** `styles.footerContainer.backgroundColor: 'transparent'` in `page-layout.tsx` is dead (overridden by the inline style on the same element).
- **P3-7** `map.tsx` applies a safe-area inset on top of `PageLayout`'s — double top padding.
- **P3-8** `index.tsx` tests its empty state against the **unfiltered** station list, so filtering to zero results shows an empty screen with no "no results" message.
- **P3-9** `report.tsx` `summaryGrid` uses `width: '48%'` with `gap: 10` — the two together over-constrain the row and the gutter is inconsistent with the 4% remainder.
- **P3-10** `report.tsx` `formatAmount` sets `minimumFractionDigits: 0` only, so `1234.5` renders as `1 234,5 ₴` — a single decimal place.
- **P3-11** Haptics are applied per call site with no policy: `Heavy` on "mark as used", `Medium` on opening a voucher, `Light` on tab presses and on the *checkout* CTA (`basket.tsx:126`). The most consequential press in the app gets the weakest haptic.
- **P3-12** `formatters.ts` contains 7 hardcoded Cyrillic strings — a utility module carrying UI copy.
- **P3-13** `ErrorBoundary` — hardcoded English `"Something went wrong"` / `"RETRY"`, unstyled relative to the rest of the product.
- **P3-14** `_layout.tsx` — hardcoded `"RETRY LOAD"` and three Cyrillic strings on the font/boot failure path, the first thing a user would see if boot fails.

---

## Notes on what genuinely works

Stated because the brief asks for criticism, not indiscriminate criticism.

- The **token and theme layer** (`tokens.ts`, `themes.ts`) is well structured. The
  problem is not the system's design, it is that the screens bypass it.
- **i18n coverage is real** — 235 keys × 4 languages, verified in sync. The
  infrastructure is finished; the adoption is not.
- **`basket.tsx`'s empty state** (P2-15) is the one screen state in the app that
  looks deliberately designed: correct proportions, one clear action, no noise.
- **`classifyVoucher`** is a genuinely good domain abstraction — the five-way
  personal/pool/gifted/blocked classification is exactly right for the B2B model.
  It is undermined only by `VoucherBadge` rendering it in off-palette 9px text.
- **`Button`** is a sound primitive. It needs adopting, not replacing.
- **`QrFullscreenModal`** is the right design for the most important surface in
  the product. It is dead code.
