# INFORMATION ARCHITECTURE — FuelFlow Mobile

> Phase 1 discovery artefact. Documents the navigation and IA **as it exists**,
> and identifies which parts are structurally wrong rather than merely
> unpolished. No restructuring is proposed here — that is Phase 2.

---

## 1. The structure as built

```
_layout.tsx  (flat Stack + hand-rolled BottomTabs overlay + app-lock gate)
│
├── /landing ......................... unauthenticated only
│
├── TAB 1  /  (index) ................ station list, search, brand filter
│     └── /station/[id] ............... station detail → fuel list      [no tabs]
│           └── /packages ............. litre packages → add to cart    [no tabs]
│
├── TAB 2  /map ...................... station map (same data as TAB 1)
│
├── TAB 3  /basket ................... cart, promocode, totals          [TABS SHOWN]
│     └── /checkout .................. auth + order + payment hand-off  [no tabs]
│           └── /payment-result ....... deep-link return                [no tabs]
│
├── TAB 4  /my-codes ................. orders + vouchers wallet
│
└── TAB 5  /profile .................. identity, theme, language, security,
      │                                legal entity
      ├── → /report .................. spending analytics
      ├── → /company ................. B2B worker roster
      ├── → /contracts ............... B2B contracts + e-signature
      └── → /invitations ............. inbound worker invitations
```

**Depth:** 3 levels maximum, but the purchase path is 5 nested screens.
**Breadth at the root:** 5 tabs.
**Orphaned branch:** 4 screens hanging off one Profile row-list.

### 1.1 Navigation mechanism

There is no router-level tab navigator. `_layout.tsx` declares a **flat `Stack`**,
and `src/components/bottom-tabs.tsx` is an absolutely-positioned overlay
(`position: 'absolute'`, `bottom: 8`, `height: 64`, `zIndex: 100`) rendered above
it.

Consequences of hand-rolling the bar:
- **No per-tab stack state.** Every tab is a `Link` to a route in one shared
  stack, so leaving and returning to a tab does not restore where you were in it.
- **Visibility is a string-prefix denylist** (`bottom-tabs.tsx:26`), not a
  navigator property.
- **No safe-area handling.** `useSafeAreaInsets()` is imported and destructured;
  the inset is never applied. `bottom: 8` is hardcoded.
- **Icon-only, no labels**, with the active state expressed as a `primaryDim`
  glow blob behind the icon rather than colour + label.

### 1.2 Where the tab bar disappears

```ts
// bottom-tabs.tsx:26
const hidePrefixes = ['/station/', '/packages', '/checkout', '/payment-result'];
```

Applied to the purchase funnel — **except `/basket`**, which sits between
`/packages` and `/checkout`. The chrome therefore goes:

| Screen | Tabs |
|---|---|
`/` | ✅ |
`/station/[id]` | ❌ |
`/packages` | ❌ |
`/basket` | ✅ |
`/checkout` | ❌ |
`/payment-result` | ❌ |

The funnel is neither a focused flow nor an in-tab journey. It is both,
alternately, which produces visible chrome flicker at each step.

---

## 2. Structural problem #1 — the entire B2B product has no place in the IA

Four screens — `/report`, `/company`, `/contracts`, `/invitations` — are reachable
only as **rows in a list on `/profile`**. Together that is:

- e-signature of legally binding supply contracts
- a worker roster with invite / fire authority
- voucher gifting and recall from a company pool
- an inbound invitation inbox
- all spending analytics

`/profile` is, by universal mobile convention, the "settings and account" drawer —
the place users go when something needs configuring, not when work needs doing.
Putting a company's operational tooling there means:

1. **A business user cannot discover the feature.** Nothing on Home, in the tabs,
   or in any empty state suggests that companies, contracts or workers exist.
2. **`/profile` is doing two unrelated jobs** — personal settings and a B2B
   dashboard — so it inevitably reads as a link dump.
3. **The invitation inbox is unreachable in practice** (see §3): the user must
   independently decide to open a settings screen to discover that a company has
   invited them.

This is not a discoverability nit. Roughly 40% of the application's screen surface
has **0% of its primary navigation**. Either the product has a business side or it
does not; the IA currently says it does not.

---

## 3. Structural problem #2 — there is no inbound channel, so state changes are invisible

Every significant event in this product originates **server-side**:

| Event | Origin | How the user learns |
|---|---|---|
Payment cleared | Monobank webhook | pull-to-refresh `/my-codes` |
Voucher issued | webhook fulfilment | pull-to-refresh `/my-codes` |
Order refunded | admin/back office | 🔴 **never** — the order vanishes (P0-2) |
Company invitation received | another user's device | only by opening a Profile row |
Voucher gifted to you | company owner's device | pull-to-refresh |
Voucher recalled from you | company owner's device | it silently disappears |
Contract available to sign | back office | only by opening a Profile row |

There is **no push notification, no in-app notification centre, no badge except
the cart count**. The only badge in the entire app is `cartCount` on the basket
tab — the one number the user already knows, because they put it there themselves.

The IA has no slot for "things that happened while you were away". For a product
built on asynchronous fulfilment and multi-party B2B actions, that is a missing
architectural layer, not a missing screen.

---

## 4. Structural problem #3 — the purchase funnel is three screens making one choice

```
/station/[id]   →   pick a fuel
/packages       →   pick a litre package
/basket         →   confirm
```

Station → fuel → package are three full-screen destinations resolving what is,
domain-wise, **one selection**: *which fuel, how much, at which station*. Nothing
in the data model requires the split — `/packages` reads its context from
`cartStore`'s persisted `selectedStation` / `selectedFuel`.

Three consequences follow directly from the split:

1. **State is smuggled through a persisted global store instead of route params.**
   `selectedStation`, `selectedFuel` and `selectedPackage` live in `cartStore` and
   are written to AsyncStorage, so a half-made choice from weeks ago rehydrates
   with stale prices (P1-7). Route params would have made the choice
   ephemeral by construction.
2. **Each screen must re-establish context**, which is why each one hand-rolls a
   header with the station name.
3. **`/packages` has to carry the whole configuration UI**, which is why each
   `PackageCard` became a 409-LOC form with its own CTA — and why five packages
   produce five competing primary actions (P1-1).

The screen split is upstream of the worst information-design problem in the
funnel.

---

## 5. Structural problem #4 — `/` and `/map` are two destinations for one dataset

Two root-level tabs render the same station collection with different
presentations and **no shared state**:

- Brand filter and search live in `/index` only.
- Selecting a station on `/map` does not inform `/index`.
- Filtering on `/index` then switching to `/map` loses the filter.

In every comparable product (food delivery, ride-hailing, EV charging, fuel apps)
list and map are a **view toggle on one screen**, because they answer the same
question — "where can I buy?" — and the user switches between them
mid-decision rather than as a separate errand.

Spending two of five root slots on one dataset is also the reason there is no slot
left for the B2B product (§2) or a notification surface (§3). The root navigation
is over-allocated to discovery and under-allocated to everything else.

---

## 6. Structural problem #5 — the wallet conflates two object types

`/my-codes` presents three sections:

1. **Pending orders** — `OrderCard`, expandable, containing `VoucherCard`s
2. **Fulfilled orders** — `OrderCard`, expandable, containing `VoucherCard`s
3. **Unassigned vouchers** — a *flat* list of vouchers, rendered by an
   **inline card defined in `my-codes.tsx:350-457`**, not by `VoucherCard`

So a voucher appears in two places, rendered by two different components, with
different accent-bar widths (5px inline vs `VoucherCard`'s border), different
"used" treatments (a `diagonalStamp` in the inline card, opacity in
`VoucherCard`, a `BlurView` in `VoucherDetailModal` — three treatments for one
state, P2-30/P2-31), and different status vocabularies (`READY` hardcoded inline
vs `getStatusConfig`'s hardcoded `'Ready'` in `VoucherCard`).

The screen is also mixing two *kinds* of object at the same level of the
hierarchy:

- an **order** is a commercial/financial record (it has a price, a status, a
  payment obligation, an ID)
- a **voucher** is a redeemable instrument (it has litres, an expiry, a QR, a
  redemption state)

A user standing at a pump wants exactly one of these — the voucher — and must
navigate the other to reach it. A user chasing a payment wants exactly the other.
The wallet serves neither cleanly because both live in one scroll.

Compounding: the section headers themselves are inconsistent (three arrangements
of icon/rule/label within one screen, P2-10), and the `SummaryBar` counts
**unfiltered** `orders.length` above a **filtered** list, so the numbers visibly
contradict the content (P0-2).

---

## 7. Additional IA issues

### 7.1 Prerequisites are enforced by ejection

`/contracts` mounts, fires `getLegalProfile()`, and on `null` shows an `Alert` and
`router.replace('/profile')`. The requirement ("you need a legal entity first") is
communicated by throwing the user out of the screen they chose (P1-17). The entry
point that led them there said nothing.

Same shape at `/checkout`: authentication is demanded *after* the user has
selected a station, a fuel, a package, a quantity and a promocode.

The IA has no concept of a **disabled or gated entry point** — everything is
tappable and some things eject you.

### 7.2 `/report` is analytics filed under settings

Spending analytics is a *use* of the product, not a configuration of it. It is
also the screen with the richest data model in the app (`summary`,
`monthlyBreakdown`, `payments`, `redemptions`) and it sits behind a Profile row,
below the theme picker.

### 7.3 No back-navigation contract

`/basket`'s back button is `router.push('/')` (`basket.tsx:49`) — a **forward
push**, not a pop, so it grows the stack every time the user "goes back".
`/report` and `/contracts` use `router.back()`. `/contracts` uses
`router.replace()` for its ejection. Three different navigation verbs for what the
user experiences as one gesture.

### 7.4 Six header implementations = six IA statements

`ScreenHeader` exists in `core/ui/` and is consumed by **one** screen
(`station/[id].tsx:38`). The other thirteen hand-roll their own, with title sizes
of 32 / 28 / 24 / 18 and back buttons of 44×44-bordered / `padding: 8`-unbordered
/ `padding: tokens.spacing.sm`-bordered-with-conditional-radius.

A header is the primary "where am I, and how do I leave" affordance. Six
implementations means the app answers that question six different ways, which is
why moving between screens feels like moving between apps.

### 7.5 Deep links

Only `/payment-result` is a genuine deep-link target. There is no deep link to a
voucher, an order, an invitation or a contract — which is precisely the set of
things a push notification (§3) would need to link to.

### 7.6 The app-lock gate has no relationship to the rest of the IA

`_layout.tsx` gates the whole tree behind an app-lock/biometric check. There is no
concept of *partially* protected content: the QR wallet and the theme picker are
equally locked. A user at a pump must clear a biometric prompt before they can
show a code they have already paid for.

---

## 8. IA scorecard

| Dimension | Assessment |
|---|---|
Root breadth | ⚠ 5 tabs, but 2 of them serve one dataset (§5) |
Root allocation | 🔴 40% of the app (B2B + analytics) gets 0 root slots (§2) |
Depth | ✅ Max 3 levels — reasonable |
Funnel length | 🔴 9 screens to one purchase; 3 screens for 1 choice (§4) |
State passing | 🔴 Persisted global store instead of route params (§4) |
Object model in the wallet | 🔴 Orders and vouchers conflated; vouchers rendered twice by two components (§6) |
Inbound / notification layer | 🔴 Does not exist (§3) |
Gating & disclosure | 🔴 Prerequisites enforced by ejection (§7.1) |
Chrome consistency | ⚠ Tab bar visibility flickers mid-funnel (§1.2) |
Back-navigation contract | ⚠ `push` used as `back`; three verbs (§7.3) |
Header consistency | 🔴 6 implementations; the shared primitive is used once (§7.4) |
Per-tab stack state | ⚠ None — hand-rolled bar over a flat Stack (§1.1) |
Deep linking | ⚠ One target, and it is the payment return (§7.5) |
Tab labels | ⚠ Icon-only, no labels, glow-blob active state (§1.1) |

---

## 9. Summary judgement

The IA is not *disorganised* — it is **mis-weighted**. The hierarchy accurately
reflects the order in which the features were built, not the order in which they
matter:

- Station discovery, built first, holds **two** of five root slots.
- The purchase funnel, built second, is **three destinations** for one decision.
- The wallet, which is the reason the product exists, is **one crowded tab** mixing
  two object types.
- The B2B product, built last, has **no slot at all** and lives in a settings list.
- The asynchronous, server-driven nature of the whole domain has **no
  representation** in the navigation whatsoever.

Read as an artefact, the navigation tells you exactly what the team's attention
history was. A user reading it learns something different: that the product is a
station browser with some extra screens attached.
