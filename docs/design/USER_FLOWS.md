# USER FLOWS — FuelFlow Mobile

> Phase 1 discovery artefact. Flows reconstructed from the source, not from a
> spec. Each step is annotated with the friction actually present in the code.
> `⚠` = friction · `🔴` = P0 from [UX_AUDIT.md](UX_AUDIT.md) · `∅` = flow does
> not exist in the product.

---

## Flow index

| # | Flow | Steps | Verdict |
|---|---|---|---|
| 1 | First launch → authenticated | 6–7 | 🔴 Blocked for non-Ukrainian speakers; dead-end on key failure |
| 2 | **Buy fuel** (the core loop) | 9–11 | 🔴 Cart destroyed if payment is abandoned |
| 3 | **Redeem at the pump** (the reason the app exists) | 5 | 🔴 Laser over the QR; remote QR with no retry; 32px close button |
| 4 | Pay for an unpaid order | 4 | ⚠ Smallest target in the app, with a side effect |
| 5 | Get a refund | — | ∅ **Does not exist.** Refunded orders vanish |
| 6 | Review spending | 3 | ⚠ Full-screen wipe on every filter change |
| 7 | B2B onboarding → signed contract | 8–10 | 🔴 Sign by unlabelled circle; 180px review window |
| 8 | B2B: invite a worker, gift a voucher | 6–8 | ⚠ Entire feature has no entry point in navigation |
| 9 | Receive & accept an invitation | 3 | 🔴 Decline is silent and irreversible; no inbound notification |
| 10 | Change language | 3 | 🔴 Unreachable before login — the screen that blocks you |
| 11 | Session expires mid-use | — | 🔴 Crash path on `/my-codes` |
| 12 | Offline / network failure | — | ⚠ No offline model anywhere; QR requires network |

---

## Flow 1 · First launch → authenticated

```
install → _layout boot (fonts, theme, app-lock gate)
        → / (index)  →  not authenticated  →  Redirect → /landing
        → landing: ВХІД ЗА ТЕЛЕФОНОМ
        → enter phone
        → SMS sent
        → enter 6-digit code
        → security_setup (device key generation)
        → УСПІШНО
        → / (index)
```

Implementation: `src/components/phone-auth.tsx` (the *old* of the two auth
components).

| Step | Friction |
|---|---|
| boot | ⚠ Failure path shows hardcoded `"RETRY LOAD"` + 3 Cyrillic strings (P3-14) |
| landing | 🔴 **20 hardcoded Ukrainian strings** (P0-10). A de/es/en user cannot read the screen, and the language setting lives behind login |
| phone entry | ⚠ Validation error is hardcoded `ВВЕДІТЬ КОРЕКТНИЙ НОМЕР` |
| OTP entry | ⚠ One field, `letterSpacing: 10`, placeholder `"000000"`. **No `textContentType="oneTimeCode"` / `autoComplete="sms-otp"`** → OS autofill of the SMS code is impossible; the user retypes it (P2-28) |
| OTP entry | ⚠ **No resend timer and no resend button.** If the SMS never arrives the flow has no recovery |
| security_setup | 🔴 **No cancel, no back, no skip** (P0-11). On keystore failure the user is parked here permanently |
| any failure | 🔴 Production error text is appended with `[SIG=… rsa=… kt=… ks=… fp=… clientValid=…]` (P0-8) |

**Assessment:** the first-run flow is the weakest-defended flow in the product,
which is the inverse of where defence should be concentrated. Three of the twelve
P0s live in these six steps.

---

## Flow 2 · Buy fuel — the core commercial loop

```
/ (index)                    station list, search, brand chips
  → /station/[id]            station detail, fuel list        [tabs hidden]
  → /packages                litre packages for that fuel     [tabs hidden]
      → "add to cart"        card locks, nothing else happens
  → /basket                  cart, promocode, totals          [tabs VISIBLE again]
  → /checkout                                                 [tabs hidden]
      → if not authenticated: PhoneAuthForm (the OTHER auth component)
      → create order server-side
      → clearCart()                          ← 🔴 BEFORE payment
      → Linking.openURL(monobank invoice)     ← leaves the app
  ← /payment-result          deep link back                   [tabs hidden]
  → /my-codes                voucher appears (via webhook)
```

**Step count to a purchase: 9 screens minimum**, 11 if authentication is
triggered at checkout.

| Step | Friction |
|---|---|
| index | ⚠ Empty state is tested against the *unfiltered* list, so filtering to zero shows a blank screen with no "no results" (P3-8) |
| index → station | ⚠ Press tilt `3deg` |
| station | 🔴 Fuel **name** is 16px `text.dim`; **price** is 28px brand-coloured. The decision attribute is the quietest element (P1-3) |
| station → packages | ⚠ Press tilt `8deg` — different physics, same gesture, one screen later (P1-4) |
| packages | 🔴 Every package is a ~350px form with its own `height: 64` filled CTA. Five packages = **five competing primary actions, no default, nothing recommended** (P1-1) |
| packages | ⚠ Price shown as `{pkg.price} ₴` (unrounded) in the header and `{(pkg.price*quantity).toFixed(2)} ₴` in the total — two formats in one card (P2-9) |
| add to cart | 🔴 `isAdded` locks the card. No toast, no route to cart, no visible count (tabs are hidden here), no continue/checkout decision. **The user's action produces silence** (P1-2) |
| → basket | ⚠ Tabs reappear mid-funnel (P1-5) |
| basket | ⚠ Renders almost entirely in the **OS system font** — every style sets `fontWeight` with no `fontFamily` (P2-5) |
| basket | 🔴 "−" at quantity 1 **silently deletes the line**, no confirm, no undo (P0-4) |
| basket | ⚠ "Remove all" is a bare 10px red text link, no confirmation (P2-19) |
| basket | 🔴 Totals are raw floats: a 15% discount can render `113.05000000000001 ₴` (P2-9) |
| basket | ⚠ The checkout CTA is `variant="secondary"` while the empty-state CTA is `primary` — inverted hierarchy between two states of one screen (P2-18) |
| → checkout | ⚠ If unauthenticated, a **visually different** OTP screen appears at peak purchase intent (P1-6) |
| checkout | 🔴 **`clearCart()` runs before the payment succeeds** (P0-1). Cancel, background, signal loss or a declined card ⇒ empty cart + unpaid order + no recovery path |
| checkout | 🔴 `isProcessing` is not reset on hand-off failure ⇒ CTA stuck disabled ⇒ force-quit |
| payment-result | ⚠ Fulfilment is asynchronous (webhook), so the return screen can only report "we're waiting" |

**Failure surface:** the two highest-risk defects in the product (P0-1, P0-4) are
both inside the paid conversion path, and the funnel's information design (P1-1,
P1-3) actively works against the decision the user came to make.

---

## Flow 3 · Redeem at the pump — the reason the product exists

Physical context: standing outdoors, one hand, possibly cold or wet, a queue
behind, a scanner in front, poor mobile signal under a canopy.

```
open app  → app-lock / biometric gate
          → / (index)
          → tap QrCode tab
          → /my-codes: scroll to find the voucher
          → tap voucher
          → VoucherDetailModal: 220×220 QR
          → hold phone to scanner
```

| Step | Friction |
|---|---|
| find the voucher | ⚠ Three sections, each with a differently-arranged header (P2-10). Vouchers live in two different places: inside `OrderCard` accordions **and** in a flat "unassigned" list, rendered by two different components (`VoucherCard` vs an inline card in `my-codes.tsx:350-457`) |
| find the voucher | ⚠ Used and unused vouchers are **both** `brandColor` (P1-10) — spent vs unspent is not encoded in colour, only in a label and opacity |
| tap voucher | ⚠ `onLongPress` does exactly the same thing as `onPress` (P1-13) |
| the QR | 🔴 **A 2px `#DC2626` line loops permanently across it** (P0-3). Decoration over a machine-readable surface |
| the QR | 🔴 It is a **remote `Image`** at a fixed 220×220 with a `#666` "QR Unavailable" fallback and **no retry** (P1-24). Under a canopy with no signal there is no QR — and the app already contains `QrFullscreenModal`, which generates it **locally as SVG at `SCREEN_WIDTH * 0.78`**, but that file is never imported |
| the QR | ⚠ 220px is small for a scanner at arm's length; no brightness boost, no fullscreen affordance |
| redemption rules | 🔴 `redemptionRules` **is fetched and discarded** (P1-8). The instructions for redeeming are on the device and never rendered |
| after scanning | ⚠ The app cannot tell the user it worked. Redemption state is either a manual self-report toggle (P1-14) or a later server refresh |
| close | ⚠ Close button is **32×32** (P1-22) |

**Assessment:** this is the flow the entire product exists to serve, and it is the
flow with the least design investment. The correct implementation exists in the
repository as dead code.

---

## Flow 4 · Pay for an unpaid order

```
/my-codes → pending orders section → OrderCard → tap "PAY"
          → Linking.openURL(invoice)
```

| Step | Friction |
|---|---|
| find it | ⚠ `SummaryBar` counts unfiltered `orders.length` while the list is filtered, so the count can exceed the visible cards (P0-2) |
| the card | ⚠ Accordion `maxHeight` is `voucherCount * 260`, a magic number that **clips** taller content with no scroll hint (P1-11) |
| tap PAY | 🔴 The button is **nested inside the header `Pressable`**, so tapping it also toggles the accordion — the card expands as the user is leaving (P1-12) |
| tap PAY | 🔴 It is the **smallest target in the card**: 9px text, 10px icon, `paddingVertical: 7` (P1-22) |
| the label | ⚠ `t('codes.unpaid') || 'UNPAID'` sits beside a raw hardcoded `'REFUNDED'` (P2-25) |
| the reference | ⚠ The order is identified to the user as `ID: ` + ten characters of a UUID (P2-23) |

The one action in the wallet that recovers revenue is the hardest thing on the
screen to hit, and hitting it does something else as well.

---

## Flow 5 · Get a refund

```
∅  This flow does not exist.
```

`Order.status` includes `'REFUNDED'`. `my-codes.tsx:187-188` filters for
`PENDING_*` and `FULFILLED || PARTIALLY_REFUNDED`. A fully refunded order matches
neither predicate and is therefore rendered **nowhere** (P0-2).

There is no refund request affordance, no refund status, no refund history, and
no notification. `PARTIALLY_REFUNDED` is handled only as an `#a855f7` accent
colour in `OrderCard` with no explanatory copy.

From the user's side: money left, an order existed, the order is gone. This is
the flow most likely to generate a support call, and it has no UI at all.

---

## Flow 6 · Review spending

```
/profile → "Report" row → /report
         → period filter (all / year / 3 months / month)
         → summary tiles, monthly table, payments log, redemptions log
         → Share2 → native text share
```

| Step | Friction |
|---|---|
| discovery | ⚠ Only reachable as a Profile list row. Nothing on Home suggests a report exists |
| filter change | ⚠ `useEffect(..., [period])` → `setLoading(true)` → **the whole screen wipes to a centred spinner** for every filter tap (P1-19) |
| tiles | ⚠ A fifth stat-tile design (P2-2); `width: '48%'` + `gap: 10` over-constrains the row (P3-9) |
| amounts | ⚠ A fourth currency format — `toLocaleString(locale, { style:'currency', currency:'UAH', minimumFractionDigits: 0 })`, so `1234.5` renders `1 234,5 ₴` with one decimal (P2-9, P3-10) |
| sections | ⚠ Expand chevrons are the Unicode characters `▲`/`▼` at 12px, in a screen that already imports lucide icons (P2-11) |
| section titles | ⚠ `fontSize: 12, letterSpacing: 4` (P2-12) |
| share | ⚠ Plain-text `Share.share()` — no PDF/CSV, which is the actual need for anyone tracking fuel spend for reimbursement or tax |

The data model here is the richest in the app (`monthlyBreakdown`, `payments`,
`redemptions`) and there is not a single chart.

---

## Flow 7 · B2B onboarding → signed contract

```
/profile → create legal entity (legal profile form)
         → /contracts
             → useEffect: getLegalProfile() → if null → Alert + router.replace('/profile')
             → contract list
             → tap the 40×40 circular PenTool button
             → signing Modal
                 → contract text in a 180px-tall scroll area
                 → optional: "read full" (dashed secondary) → SECOND Modal on top
                 → SignaturePad
                 → submit
```

| Step | Friction |
|---|---|
| discovery | ⚠ No entry point outside a Profile row. A business user has no way to learn the feature exists |
| gating | 🔴 The prerequisite is enforced **as a side effect**: mount → alert → ejected to `/profile` (P1-17). The user discovers the requirement by being thrown out of the screen |
| the sign button | 🔴 **40×40 circular, icon-only, no label**, for the most legally significant action in the product (P0-5) |
| review | 🔴 `sheetScroll: { maxHeight: 180 }` — about six lines. The real text is behind a **second modal stacked on the first**, reached via a dashed-border button that reads as tertiary (P0-5) |
| consent | 🔴 **No "I have read and agree" checkbox and no scroll gate.** The signature can be submitted without the text ever being scrolled |
| visual language | ⚠ Cards are `borderRadius: 2` (sharp) while the sibling `/invitations` screen uses `12`/`10` (rounded). Two B2B screens one tap apart in **opposite shape languages** (P2-4) |
| radii | ⚠ 2, 4, 12, 20 and 22 all appear in this one file, none from tokens |
| auto-select | ⚠ `setSigningContract(available.length === 1 ? available[0] : null)` — the sheet's behaviour changes silently with the number of contracts (P3-4) |
| afterwards | ⚠ No record of what was signed is shown back to the user |

---

## Flow 8 · B2B: invite a worker, gift a voucher

```
/profile → /company
         → worker roster
         → invite (phone number) → Alert
         → worker accepts (their device)
         → gift a voucher from the company pool → worker
         → optionally recall the voucher
         → optionally fire the worker
```

| Step | Friction |
|---|---|
| discovery | ⚠ Same as Flow 7 — a Profile row, nothing else |
| roster | ⚠ Yet another stat-tile design and another chip design (P2-2, P2-3) |
| gift | ⚠ The gifted voucher is classified `gifted_to_worker` and rendered with `VoucherBadge`'s hardcoded off-palette `#a855f7` at 9px `Inter-Black` (P2-8) |
| gift | ⚠ On the owner's side a gifted voucher shows `→ WorkerName` in `Inter-Medium` — **a font weight the app never loads** (P2-6) |
| recall / fire | ⚠ Destructive, multi-party actions whose confirmation depends on whichever `Alert.alert` the call site happened to add (P1-16) |
| feedback | ⚠ Success and failure both arrive as OS dialogs |

`classifyVoucher`'s five-way model (`personal` / `company_pool` /
`gifted_to_me` / `gifted_to_worker` / `blocked`) is a genuinely good abstraction.
The UI expresses it with a 9px badge in three hardcoded Tailwind colours.

---

## Flow 9 · Receive and accept an invitation

```
(a worker is invited on someone else's device)
  → ??? the invited user is never notified in-app
  → /profile → "Invitations" row → /invitations
  → accept → Alert confirmation
  → decline → nothing but a haptic; the invitation is destroyed
```

| Step | Friction |
|---|---|
| notification | 🔴 **There is no inbound signal at all** — no push, no badge on Profile, no badge on the tab bar, no banner. The user must independently decide to visit a Profile row to discover that a company has invited them |
| the two buttons | 🔴 Accept and decline are the same size, same weight, adjacent |
| decline | 🔴 **Irreversible, unconfirmed, and gives less feedback than the safe action** (P0-6) |
| the dates | ⚠ `formatExpirationDate(inv.createdAtUtc)` printed bare and unlabelled — a *creation* date passed through a function named for *expiry* (P2-24) |
| in-flight state | ⚠ `isBusy = acceptMutation.isPending \|\| declineMutation.isPending` dims **every** card, so the user cannot tell which invitation is processing (P1-18) |

---

## Flow 10 · Change language

```
/profile → language selector → uk / en / de / es
```

Three taps, and the mechanism works — 235 keys × 4 languages, verified in sync.

🔴 **But `/profile` requires authentication, and the login screen is hardcoded
Ukrainian** (P0-10). The setting that would make the app readable is behind the
screen that is unreadable. For a de/es/en-speaking new user this is a closed loop.

Additionally, 18 of 37 files bypass `t()` entirely, so even after switching, the
user meets `READY`, `REDEEMED`, `LITERS`, `RETRY`, `ADDRESS`, `BUILD ROUTE`,
`QR Unavailable`, `Something went wrong` and `⟳ REFRESH` in English regardless of
selection.

---

## Flow 11 · Session expires mid-use

```
token expires while /my-codes is mounted
  → isAuthenticated flips false
  → my-codes.tsx:171  return <Redirect href="/landing" />
  → the useMemo at line 190 is skipped
  → hook count changes between renders
  → React: "Rendered fewer hooks than expected"
```

🔴 P0-7. This is not theoretical: `/my-codes` is the screen a user leaves open
longest (they open it and hold the phone up), so it is the screen most likely to
be mounted when a token expires.

Compounding: `bottom-tabs.tsx:18-20` derives auth from **two sources** OR'd
together (`storeAuth || hookAuth`), so during the transition the tab bar and the
route can disagree about whether the user is logged in (P2-17).

---

## Flow 12 · Offline / degraded network

```
∅  There is no offline model.
```

- The QR at the pump is a **remote image with no retry and no cache**
  (P1-24) — the single worst place in the product to require the network.
  Petrol station canopies are notorious signal dead zones.
- `cartStore` persists to AsyncStorage, but that persistence is a liability rather
  than an asset: it preserves **stale prices** indefinitely with no freshness
  check (P1-7).
- `report.tsx`, `my-codes.tsx` and `contracts.tsx` each hand-roll their own
  loading/error/retry, so the offline experience differs per screen (P1-19).
- Error recovery is `Alert.alert` plus, on some screens, a bare `Text` "retry"
  link (`report.tsx:189`, `202` — `padding: 12` around a plain word).

---

## Cross-flow observations

1. **The funnel is 9 screens deep for one purchase.** Station → fuel → package are
   three screens making one product choice; nothing in the domain requires them to
   be separate destinations.

2. **The two most important flows are the two least designed.** Flow 3 (redeem)
   has decoration harming function and a remote-image QR; Flow 2 (buy) destroys the
   cart on abandonment. Flow 6 (report) and Flow 7 (contracts) — both secondary —
   have had more layout attention.

3. **Every flow terminates in an `Alert.alert` or in nothing.** There is no
   success state that belongs to the product. Because there is no toast primitive,
   feedback is either an OS dialog or absent — and which one you get was decided
   per call site.

4. **Destructive actions are consistently cheaper than safe ones.** Delete a cart
   line: one tap on "−", no dialog. Decline an invitation: one tap, no dialog.
   Clear the whole cart: one tap on a text link, no dialog. Apply a promocode: a
   bordered button. Read a contract: two nested modals.

5. **Prerequisites are discovered by ejection, not by disclosure.** `/contracts`
   throws you to `/profile`; `/checkout` demands login after all the work is done.
   Nothing tells the user what is required before they invest effort.

6. **There is no inbound channel.** No push, no in-app notifications, no badges
   except the cart count. Order fulfilment is a webhook the user learns about by
   pulling to refresh; a company invitation is only visible if the user goes
   looking for it. For a product whose central events (payment cleared, voucher
   issued, invitation received, voucher gifted, voucher recalled) all originate
   **server-side**, the absence of any notification model is an architectural gap
   as much as a UX one.
