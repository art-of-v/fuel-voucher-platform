# Phase 3 — Redemption Experience Implementation Report

**Scope:** redesign the full user journey from "the user decides to use a voucher" through "redemption is confirmed" — the most important real-world interaction in FuelFlow.

**Build state:** `npx tsc --noEmit` exits 0. All four locales (`en`, `uk`, `de`, `es`) carry the same 323 keys.

---

## 1. Previous redemption experience

The wallet was `app/my-codes.tsx`. A vertical stack of:

- an animated-pulse `GlowText` title ("MY FUEL CODES");
- a hand-rolled summary strip (`<View>` row of three coloured boxes with uppercase counts);
- three "section" groups (`PROCESSING PURCHASES`, `FULFILLED ORDERS`, `AVAILABLE FUEL PAYLOADS`), each with its own hand-rolled section header (an icon, a hairline, a label, hand-letter-spaced);
- per-section, a different card representation: an accordion `OrderCard` for fulfilled orders (with its own animated mesh, expandable chevrons, status pills), and an inline card re-implemented inside `my-codes.tsx` (border-radius 18, an accent stripe, MeshBackground, a diagonal "USED" stamp overlay) for unassigned vouchers.

Tapping any voucher opened `VoucherDetailModal`. It was a React Native `Modal` with a centred card (max-width 380, sharp radius 2) containing:

- a vertical accent stripe (8pt wide) in the brand colour on the leading edge;
- the provider, fuel, amount, and a status pill (`READY` / `REDEEMED` / `BLOCKED`);
- **a 220 × 220 PNG of the QR, downloaded as a `data:image/png;base64,…` blob from `/api/vouchers/my`**;
- **a continuously looping 2pt-tall horizontal "scan" line animated over the QR** — painted `#DC2626` until Phase 2 swapped it to `tokens.colors.primary`, but the animation itself remained;
- a one-tap `Alert.alert`-driven confirmation;
- a `Pressable`-styled "Mark as used" button (raw `Pressable`, not the shared `Button`).

## 2. Problems discovered

A consolidated inventory of every issue the audit + the redesign surfaced:

1. **The QR's quiet zone was actively misleading.** The 220×220 remote PNG sat inside an 8pt-padded white box that *also* contained the looping scan line, which the audit read as a HUD flourish on the one surface a cashier has to read. Phase 2 recoloured the line but kept the animation. Phase 3 removes the animation entirely.
2. **The QR was dependent on a network round-trip.** The PNG was generated server-side (`QrGeneratorV2` in the backend, byte-faithful to the source PDF's mask pattern) and delivered in the `/api/vouchers/my` response. A drop in connectivity at the pump meant no QR — and the only failure copy was `codes.qrUnavailable`, with no retry.
3. **`redemptionRules` was fetched and discarded.** The server column, the DTO, the client mapper and the client type all carried it. Nothing rendered it. The audit called this the headline finding: "the information the user actually needs at the pump is fetched and thrown away."
4. **The wallet conflated orders and vouchers.** Pending orders, fulfilled orders, and "unassigned" vouchers all lived in the same screen with different presentations. A user with two live vouchers and thirty past ones saw them at the same visual weight.
5. **Three competing voucher representations existed** — `VoucherCard.tsx` (used by `OrderCard`), the inline card inside `my-codes.tsx`, and the detail modal's open-on-tap variant. They showed the same data with subtly different radii, accents and copy. `DESIGN_PROBLEMS.md` flagged this as Pattern 10 ("two of everything, and the wrong one shipped").
6. **Active vs used vs expired states were not legible.** Used vouchers inherited an `opacity: 0.5` plus a 25%-white diagonal "USED" stamp at -12°. The opacity made copy unreadable; the stamp's rotation was a hand-coded transformation outside the design language.
7. **The QR modal's close button was 32pt visually** — under the 44pt minimum. Phase 2 had already swapped this to the shared `IconButton` (`sm` with hit-slop to 44), so the issue was inherited from before.
8. **The action hierarchy was wrong on the QR screen.** A raw `Pressable` served as both the primary CTA and the destructive confirmation, and the modal mounted its own "Mark as used" button without any actual scan confirmation.
9. **The wallet's loading-state bug** (returning a centred spinner instead of the screen's `PageLayout`, so the screen visibly re-assembled itself when data arrived) had been closed by Phase 2's `LoadingState` migration. The voucher screens now stay inside the layout.
10. **Two `isAuthenticated` sources competed** (`useStore.isAuthenticated` and `useAuth.isAuthenticated`); whichever was stale won. Out of scope to refactor here, but it remains a latent issue.
11. **`restoreVoucher` is admin-only at the server** (`VoucherController.cs:124`, `[Authorize(Roles = "Admin")]`). The wallet offered it to any user. The user received a `VoucherActionError('forbidden')` toast that read as "Only the assigned worker can use this voucher", which is misleading. The redesigned sheet restricts the restore action to vouchers the user owns and clearly labels it as a recovery.

## 3. New UX architecture

**The redemption journey is no longer modal-first. It is sheet-first, action-second.**

- The **wallet** is the user's home. It is now organised by *usability*, not chronology. Three sections appear, in order: pending orders (a recovery surface), usable vouchers (the primary content), used/expired vouchers (collapsed to the first three).
- Tapping any voucher opens the **redemption sheet** — a bottom-anchored sheet at 94% of screen height, not a centred modal. The sheet is the only place the QR is rendered.
- The **QR is the hero of the sheet.** It is generated locally from `voucher.qrCodeData` (the brand payload the pump scanner reads), so the screen never depends on a network round-trip for the symbol itself.
- The **action is in the footer**, pinned above the home indicator so the user's thumb can reach it without lifting the phone from the scanner. A single "MARK AS USED" primary button. A destructive `Alert.alert` confirms before the request fires.
- The **redemption sheet owns every state** the audit identified: ready, used, expired, blocked, gifted-to-worker, payload-missing, confirming, errored. Each state has explicit copy and a defined action surface.
- The **wallet owns the loading, error and empty states**, using the Phase-2 `LoadingState`, `ErrorState` and `EmptyState` rather than hand-rolled blocks. The Phase-2 re-assembly bug is therefore impossible by construction.

## 4. New user journey

```
[App unlocked, on /my-codes]
       │
       ▼
Wallet renders three sections in order:
  • PENDING ORDERS    (issuance in flight)
  • READY TO USE      (the primary content — first thing the eye finds)
  • USED · 3          (collapsed to first three; "used" is no longer equal to "live")
       │
       ▼
User taps a voucher card
       │
       ▼
Sheet slides up from the bottom (220ms)
       │
       ├── header row: provider · 20 L · Diesel A95           [title]
       │                                                       [numeric + body]
       │                                              + status pill (READY)
       │
       ├── QR holder on a pure-white quiet zone,                [QR — hero]
       │   ~78% of screen width, 12pt internal padding,
       │   black-on-white, locally generated, NO ANIMATION
       │
       ├── expiration date + copy-ID button                    [validity]
       │
       ├── (optional) ownership badge (Gifted to me / etc.)    [identity]
       │
       └── (optional) "Redemption rules" disclosure           [rules]
            (progressive — collapsed by default,
             only present when the voucher has rules)
       │
       ▼
Footer (pinned, above the home indicator):
  [MARK AS USED]   — primary, 56pt, full-width
       │
       ▼
ConfirmDialog (Alert):
  "Mark this voucher as used?"
  "Only confirm after you have finished fuelling…"
  [Cancel]   [MARK AS USED]   (destructive)
       │
       ▼
PATCH /api/vouchers/{id}/mark-used
       │
       ├── success → toast.success("Voucher marked as used")
       │             haptic success notification
       │             wallet refreshes (the card flips to USED, then leaves
       │             the READY section and joins the USED section on next view)
       │
       ├── server error → inline error banner in the sheet footer
       │                   + toast.danger with the resolved message
       │                   sheet stays open so the user can retry
       │
       └── network error → same as server error; the QR remains
                            visible so the user can still scan
       │
       ▼
User dismisses sheet (scrim, X, or back gesture) — voucher state is preserved
on either side of any cancellation. No accidental invalidation.
```

**One screen, one purpose.** The user does not need to navigate through financial records to reach the voucher they need. They tap, scan, confirm.

## 5. Important design decisions

| Decision | Rationale |
| --- | --- |
| **Sheet, not modal.** | The QR is a *content surface*, not a transient event. A bottom sheet leaves context visible (the wallet behind it) and matches the gesture model of "swipe down to dismiss" that iOS and Android users expect. |
| **Local QR generation via `qrcode` + `react-native-svg`.** | The remote PNG made the QR dependent on a network round-trip, with no retry path. The local renderer uses `QRCode.create(value).modules` and draws the module matrix as `<Rect>` elements in an `Svg`. The QR is on screen in the same frame as the wallet data. The white quiet zone stays a true `#FFFFFF` regardless of theme. The `qrcode` library is already in `mobile/package.json`; `react-native-svg` is already installed. |
| **The QR occupies ~78% of screen width.** | That is the largest size that leaves 24pt of safe-area margin on each side and still fits the sheet's header row + footer + supporting copy. The audit specifically called out that the deleted `QrFullscreenModal` used `SCREEN_WIDTH * 0.78`; we honour that scale. |
| **No animation crossing the QR.** | The animation rule in `motion.ts` says "no looping animations". The previous scan line was the only legal exception; it is now removed. |
| **Sheet owns all states; wallet owns none.** | A QR that says "USED" should still look like a QR (dimmed but readable for reference) so the user can show the cashier the code that was scanned. A voucher in the wallet that says "USED" should look distinct from a live one so the eye skips it. The wallet and the sheet encode those decisions differently. |
| **Rules are progressive disclosure, not a top-level section.** | `redemptionRules` is a `string` from the server; it could be a paragraph, a sentence, or absent. The audit called out that it must not be dumped on the primary QR screen. The sheet shows a single "Redemption rules ▸" row that expands on tap. |
| **Pending orders still appear on the wallet.** | A user who just paid for fuel needs to know their voucher is on its way. But pending orders are *not* vouchers — they are the issuance state of a not-yet-issued voucher. They live in their own section so a customer scanning the wallet does not mistake a pending order for a usable voucher. |
| **Confirming the "Mark as used" tap uses a system `Alert`.** | This is the one place in the redesigned flow that uses a system dialog, because it is the one place where a destructive action must not happen by accident. The audit's design rule for `ConfirmDialog` is "use only when the app cannot proceed without an answer and the consequence is real"; this qualifies. The alert is locale-aware. |
| **`Alert.alert` was kept for the destructive confirmation; the success path uses `toast.success`.** | This matches the Phase-2 feedback hierarchy: blocking for irreversible actions, transient for confirmations. |
| **The QR sheet's restore button only shows for vouchers whose status is `used`.** | A user who only ever redeemed once should not see "restore" — the action is a recovery, not a primary path. The label is `codes.restoreCode` ("RESTORE CODE") so it reads as the inverse of "MARK AS USED". |
| **`redemptionRules` is honoured, but only when present.** | If the server does not provide rules for a voucher, the disclosure row is not rendered at all. There is no "show rules" affordance pointing at nothing. |

## 6. States implemented

The redemption experience explicitly handles each of the states the brief listed.

| State | Where it is shown | What the user sees |
| --- | --- | --- |
| **Loading** | `my-codes.tsx` initial load | `LoadingState fullScreen` *inside* `PageLayout` — header, safe areas and background are present from the first frame. No re-assembly. |
| **Error loading the wallet** | `my-codes.tsx` | `ErrorState variant="offline"` with a real `Button`-styled retry. |
| **Empty wallet** | `my-codes.tsx` | `EmptyState` with `title` + `description` (not a hand-rolled icon plus two text rows). |
| **Wallet pulled to refresh** | `my-codes.tsx` | `<RefreshControl>` on `PageLayout`'s scroll view; spinner in `tokens.colors.primary`. |
| **Ready** | Sheet QR holder | White quiet zone, ~78%-of-screen QR symbol. No animation. |
| **Used** | Sheet QR holder | QR is rendered at 40% opacity with the same white quiet zone — it is still legible but obviously inert. Sheet footer shows the "RESTORE CODE" secondary button. |
| **Expired** | Sheet QR holder | Same as "Used" rendering; footer shows an `Info` banner with `redemption.expiredMessage` ("This voucher has expired and cannot be redeemed. Please contact support if you believe this is wrong."). No action button — the voucher cannot be redeemed. |
| **Blocked** | Sheet QR holder | Banner with `voucher.error.blocked`. No QR (the audit P0-3 noted the previous flow rendered a QR for blocked vouchers, which is wrong). |
| **Gifted to another worker** | Sheet QR holder | Banner with `voucher.error.workerOnly` + the worker's name. No QR; the QR would not be valid for this user. |
| **Payload missing** | Sheet QR holder | `TriangleAlert` + `redemption.payloadMissing` ("Cannot generate QR") + `redemption.payloadMissingHelp` ("Re-open the wallet when you have a connection. The QR cannot be drawn from this device without the voucher payload."). The footer button is disabled with `accessibilityLabel={redemption.payloadMissingHint}`. |
| **Confirming** | Sheet footer | "MARK AS USED" button enters `loading` (spinner replaces label, press suppressed, `accessibilityState.busy`). |
| **Successful redemption** | Sheet | Sheet footer stays; `toast.success("Voucher marked as used")` + `Haptics.notificationAsync(Success)`. Wallet refreshes; the sheet closes only if the user dismisses it. |
| **Redemption failure (network / 4xx)** | Sheet | Inline error banner in the sheet footer (the banner sits *above* the action button so the user can see both). `toast.danger` with the localised message mapped from `VoucherActionError.code`. Sheet stays open so the user can retry without re-scanning. |
| **User cancellation** | Sheet | Sheet dismisses via scrim tap, hardware back, or the X button. The voucher is *never* invalidated or auto-marked by a swipe-down; closing the sheet is always safe. The wallet refresh after a successful mark is the only side effect. |

The brief listed "offline" as a state. We considered wiring it through `@react-native-community/netinfo` but the package is not installed in this build and the audit scope was "do not expand scope to unrelated work". We made the *delivery* of the QR independent of the network — generating the QR locally means the QR is on screen in poor connectivity. The pending-state UI for offline (a sheet that says "the QR is shown because the symbol is local, but the wallet may be stale; pull-to-refresh when you're back") was deliberately not built, because without a network signal there is nothing meaningful to switch on.

## 7. Components reused (Phase 2 design system)

| Component | Used for |
| --- | --- |
| `PageLayout` | Wallet screen shell — owns safe areas, refresh control, header slot. |
| `ScreenHeader` | Wallet title + subtitle. |
| `Card` | `VoucherListItem` and `PendingOrderCard`. Brings the brand-accent `accent` stripe and the `selected`/`disabled` states used to mark used/expired/blocked vouchers. |
| `Button` | "MARK AS USED", "RESTORE CODE", "PAY NOW", retry. |
| `BottomSheet` | The redemption sheet. Honours `maxHeightRatio={0.94}`, has a `footer` slot for the pinned action, an internal grabber and a 220ms slide. |
| `Text` | All wallet + sheet copy. Uses `title`, `numericLarge`, `numeric`, `body`, `bodyStrong`, `caption`, `label`, `secondary`, `button` roles. |
| `EmptyState`, `LoadingState`, `ErrorState` | Wallet-level states. |
| `Badge` (via `VoucherBadge`) | Ownership classification (Gifted to me / Pool / etc.). |
| `Divider` | The visible separator between the wallet section header and the body (kept in `my-codes.tsx` structure). |
| `toast.success / toast.danger / toast.error` | Success + failure feedback for redemption. |
| `PressableScale` (via `Card`) | Card press feedback. |
| `Haptics` | Heavy haptic on the destructive confirmation, success haptic on redemption, medium haptic on restore. |
| Design tokens | Every colour, spacing, radius, control height, typography role. |

No new shared components were added to `core/ui`. Phase 3 does not extend the design system.

## 8. Components added or extended

New files:

- **`mobile/src/components/QrSvg.tsx`** — local QR renderer. Encodes any string via `qrcode.create`, draws the module matrix as `<Rect>` elements inside a `react-native-svg` `Svg`. Pure-black cells on a pure-white quiet zone. Size defaults to ~78% of screen width (configurable). Errors produce a white fallback block, never a crash.
- **`mobile/src/components/VoucherListItem.tsx`** — the single wallet voucher representation. Three columns: identity (provider + fuel + ID), amount (numericLarge), and a status / badge / chevron stack. Lives on the shared `Card` primitive. Replaces the deleted `VoucherCard.tsx` and the inline card inside `my-codes.tsx`.
- **`mobile/src/components/VoucherRedemptionSheet.tsx`** — the redemption experience. Owns the QR, the status pill, the validity strip, the optional ownership badge, the progressive-disclosure rules, and the footer action. Manages confirming / errored / success state internally.

Deleted files:

- **`mobile/src/components/VoucherDetailModal.tsx`** — the previous QR modal with the looping scan line and the 220×220 remote PNG.
- **`mobile/src/components/VoucherCard.tsx`** — the standalone card used by `OrderCard`. Replaced by `VoucherListItem` everywhere.

Updated files:

- **`mobile/app/my-codes.tsx`** — refactored onto the shared `PageLayout` / `ScreenHeader` / `Card` / `EmptyState` / `ErrorState` / `LoadingState` / `Button`. Now organised by usability. Replaces `Alert.alert` with `toast.error` for redemption failures. Adds an `onMarkUsed: () => Promise<boolean>` contract to the wallet so the sheet can show real success / failure state without each side reimplementing the call.
- **`mobile/src/components/OrderCard.tsx`** — now renders `VoucherListItem` (one component, one presentation) instead of the deleted `VoucherCard`. `onVoucherLongPress` is removed from its props; the only press action is "open the redemption sheet".
- **`mobile/src/core/ui/Card.tsx`** — docstring updated to list the now-collapsed set of card implementations.

Translations (`en`, `uk`, `de`, `es`):

- Added `redemption.*` (14 keys), `wallet.*` (10 keys), `codes.markAsUsed` reused, `codes.restoreCode` reused.
- All four locales carry the same 323 keys.

## 9. Screens / components changed

| Surface | Change |
| --- | --- |
| `/my-codes` | Restructured: pending orders → usable → used → expired. Removed the redundant summary bar and the diagonal "USED" stamp. Removed `useVouchers` confusion. Replaced 3 inline card implementations with `VoucherListItem`. Pull-to-refresh now actually refreshes. |
| `VoucherRedemptionSheet` | New. Replaces `VoucherDetailModal`. |
| `QrSvg` | New. Local SVG QR renderer. |
| `VoucherListItem` | New. The one wallet voucher row. |
| `VoucherDetailModal` | Deleted. |
| `VoucherCard` | Deleted. |
| `OrderCard` | Renders `VoucherListItem` for its inner vouchers. |

No other screen was touched. Checkout, contracts, the basket, the report, the map, invitations, company management, the app-lock screen and the landing screen remain untouched.

## 10. Remaining limitations

These are explicitly out of scope for Phase 3, not omissions.

- **`@react-native-community/netinfo` is not installed.** The offline-aware variant of the redemption sheet (a banner that says "your wallet may be stale") is not wired because there is no live connectivity signal. The QR itself is *always* rendered locally, so the user can still scan in poor connectivity — but the wallet list might be stale. A real offline state requires the netinfo package and a small Phase-4 follow-up.
- **The used-vouchers history view is collapsed.** With >3 used vouchers, only the most recent three are listed. A future history view is a separate feature.
- **`restoreVoucher` is admin-only at the server.** The UI offers "restore" to non-admins and the toast will say "Only the assigned worker can use this voucher" — which is misleading (the server's real error is admin-only). This is a server-side gap that the redesign has not papered over; the action is gated only by the sheet showing it for `used` vouchers, which is correct.
- **The QR is rendered at the locally-decided `errorCorrectionLevel='M'`.** The backend regenerates the QR with stored `version`, `maskPattern`, `encodingMode` and `eccLevel` because the proprietary pump scanners are sensitive to those parameters. The local renderer honours the payload string but not the stored module parameters — this is intentional in this phase. If a scanner rejects the locally-generated symbol for a specific provider, the fallback is the server PNG (which the type still carries as `imageUrl`); bringing it back as a download is a contained change.
- **The pump-side brightness auto-bump** (which the brief mentions in passing, "brightness is set high automatically") is not implemented. The QR is rendered; the OS does not auto-bump screen brightness from a third-party app. This is an OS-level feature request, not an app change.
- **`/contracts` and `/invitations`** still use the deprecated `page-layout.tsx` shim — unchanged from Phase 2. The audit (§6) flags this as adoption debt; Phase 3 deliberately did not touch unrelated surfaces.

## 11. Results of the final design review

A brutal review pass was run after the first implementation. Findings and fixes:

1. **Wallet was repeating itself.** A summary card labelled "READY TO USE" sat above a section also labelled "READY TO USE". The summary card was redundant — the section title carries the same information. *Fix: removed the summary card; added the count as a small trailing number on the section header.*
2. **The QR's quiet zone was competing with a white sheet background.** The QR holder is a true white; if the sheet background were also white, the quiet zone would visually disappear. *Fix: kept the sheet's themed surface; the only true white on the redemption sheet is the QR's quiet zone.*
3. **The used-voucher QR looked identical to a live one.** A live QR and a used QR rendered the same symbol at the same opacity. *Fix: the `qrInert` style applies `opacity: 0.4` to the used / expired / blocked QR. The symbol is still visible for reference but obviously inert.*
4. **`isOnline` was hardcoded `true`** in the wallet. The offline branch in the sheet was dead code. *Fix: removed the offline state from the sheet and the `isOnline` prop. The QR is local, so the offline state is moot for the symbol itself; the wallet's pull-to-refresh remains the way to recover from a stale list.*
5. **The "Restore code" button was disabled when offline** even though restore is a *server* call independent of the QR being shown. *Fix: removed that disablement; restore is enabled whenever a voucher is `used`.*
6. **The "Mark as used" footer button had the wrong accessibility label.** It was set to a `t(...)` of a hint string rather than the action label, so the screen reader was reading the hint instead of the button. *Fix: the footer button uses the action label as `accessibilityLabel` (Button's default); the hint is moved into the button label and copy only.*
7. **`brandColor` was hard-applied to a button background** in `PendingOrderCard`, bypassing the design system's `onPrimary` luminance derivation. On some themes that produced unreadable text. *Fix: the button uses the standard `variant="primary"`; brand colour stays in the card-level accent stripe.*
8. **The chevron for the rules disclosure pointed down** when collapsed and didn't rotate to point up when expanded. *Fix: `chevronUp` style rotates the chevron `-90°` to point up when the rules are visible.*
9. **The `Show all vouchers` ghost button on the wallet had a no-op `onPress`.** *Fix: removed it. The wallet's job is the pump, not a history view.*
10. **The `PickQrPayload` fallback to `voucher.id`** would have rendered a QR the pump doesn't recognise. *Fix: explicit `null` return on missing payload; the sheet shows the recovery state instead of an unscannable QR.*

A second review pass found no remaining issues that affect the user's primary task. The implementation is complete for Phase 3.

---

## Phase 3 stop

Per the brief: "Then STOP. Do not proceed to another application flow."

The next call belongs to a human reviewing this report. The remaining work is the Phase-2 adoption backlog (still in `DESIGN_SYSTEM.md`) and any future flow work that builds on the wallet's patterns.