# Mobile maintainability: findings and plan

An audit of `mobile/` for **architecture, readability and maintainability for
humans** — not for a linter, not for speed. Written October 2026.

This is a **living tracker**. Every item carries its status, its PR, and — while
work is in flight — enough detail to resume from. Update the row when an item
lands; delete the "work in flight" notes once they are done.

How the code *looks* is in [`DESIGN.md`](DESIGN.md). How it is *built* is in
[`MOBILE-ARCHITECTURE.md`](MOBILE-ARCHITECTURE.md). This file is the list of
what still needs fixing.

---

## What was measured, and how

Sizes, counts and patterns were taken from the code, not estimated. Anything
below stated as a number was recounted against `main` at the time of writing —
so treat the figures as a snapshot, not a contract. They will drift.

The useful distinction throughout is **length versus separability**. `radar.ts`
is 286 lines of pure arithmetic with a 258-line test beside it. `profile.tsx`
was 863 lines inside one function with nine pieces of state. Same order of
magnitude, completely different cost to a human.

---

## Findings

### What is already good, and must not be broken

These are real strengths. Any plan that damages them is the wrong plan.

| | Evidence |
|---|---|
| Type discipline | 11 `as any` across 21k lines, zero `@ts-ignore`, zero `@ts-nocheck` |
| Design system | `DESIGN.md` is 758 lines of enforced rules, plus token scales in code |
| Network access is centralised | react-query throughout; `fetch` appears only in `core/api` and two documented exceptions |
| UI import discipline | 27 files import `core/ui` through the barrel, **zero** reach past it |
| Feature boundaries | `features/` has **zero** cross-feature imports |
| Tests are next to code | 24 suites, `npm test` green, located beside what they test |
| Locales are in sync | 461 keys, identical across en/uk/de/es — enforced by `parity.test.ts`, not by reading |

### What needs fixing

| # | Finding | Size | Severity |
|---|---|---|---|
| 1 | God screens — one function holding an entire screen | `map.tsx` 1343 lines / 3 fns — **the only one left** | **High** |
| 1b | Screens still one function after extraction | `my-codes.tsx` 899, `company.tsx` 331 — sections moved out, functions not yet split | Medium |
| 2 | Screen tests | 3 of 19 screens covered, 44 tests | **Critical** |
| 3 | Design system ignored by screens | 306 hard-coded values left, in the 10 screens still on `GRANDFATHERED_SCREENS` | **High** |
| 4 | Architecture half-migrated - voucher UI in `src/components/`, its data in `features/vouchers/` | **resolved** - all voucher UI now sits in `features/vouchers/components/` | Done |
| 5 | Haptic policy lives in screens | 100 call sites | Medium |
| 6 | No architecture document | — | Medium |

**Read finding 1 by functions, not lines.** Line counts stopped meaning what they
meant in October 2026: [#835](https://github.com/art-of-v/fuel-voucher-platform/pull/835)
reformatted the app to 2-space indent at a 100-column width, which grew
`my-codes.tsx` from 799 to 1456 lines and `company.tsx` from 753 to 1041 **without
adding a single statement**. Comparing content across the two commit ranges shows
the non-blank, non-comment line count is identical. A file can double in size and
change nothing, so the number of top-level functions is the metric that survives
formatting — that is what the column counts.

---

## Plan

Ordered by value per unit of risk. Items 1-3 are guard rails: they stop the
debt from growing while the slower items are worked through.

### 1. Guard the translations ✅

**Why first.** `t()` returns the key itself when a translation is missing, so a
forgotten string reaches the user as `renew.pay` on a button. Silent, and visible
in one language only. Cheapest fix with the widest protection.

**Done** — [#788](https://github.com/art-of-v/fuel-voucher-platform/pull/788), merged.

15 assertions over the shape of the four locale files: key parity, no blank
values, matching `{n}` placeholders, no key echoed back as its own value. Verified
by mutating each file in turn and confirming the right assertion failed each time.

### 2. Stop hard-coded values in new screens ✅

**Done** — [#795](https://github.com/art-of-v/fuel-voucher-platform/pull/795), merged.

`tokens/use-design-tokens` names the specific token when one exists, and says so
plainly when one does not — it will not tell a developer to write
`tokens.spacing.md` when the value is 14.

Two design decisions worth remembering:

- ESLint's suppression files were tried first and **do not work here**: they count
  per file, so one added literal makes every pre-existing violation in that file
  reappear. `my-codes.tsx` has 154, so any edit would bury the author. Hence an
  explicit `GRANDFATHERED_SCREENS` list — a migration list where deleting a line
  is the whole migration step for that file.
- `app/station/[id].tsx` needs `\\[id\\]` escaped. These are globs; a bare `[id]`
  is a character class and the file would silently stop being exempt.

The rule also surfaced that `map.tsx` runs its own z-index ladder (90, 100, 120,
150, 200) that matches no token.

### 3. Write the architecture document ✅

**Done** — [#796](https://github.com/art-of-v/fuel-voucher-platform/pull/796), merged.

`MOBILE-ARCHITECTURE.md`, organised around "where does my code go?". Includes a
"rules you cannot guess" section — polling gated on `useAppStateActive()`, error
codes not messages, `Link asChild` discarding styles — each stating whether a
machine catches you breaking it. Only two of the seven are automated.

Three claims in the first draft were false and are corrected: `fetch` **is**
called outside `core/api` in two places, four screens (not seven) bypass the
hooks layer, and `core/` **does** import `features/` in one spot.

### 4. Split `profile.tsx` ✅

**Why.** The clearest case of finding 1: one function of ~840 lines with nine
pieces of state. It could not be searched, reviewed in one pass, or tested.

**Done in two PRs — 916 → 237 lines, all eight sections now components.**

#### PR A — body sections

[#801](https://github.com/art-of-v/fuel-voucher-platform/pull/801), merged.
`ProfileHeaderCard`, `ManagementSection`, `ActivitySection`,
`PreferencesSection`, `AccountActions`. Verified as a pure extraction by mechanical
comparison: 25 distinct `t()` keys in and out, 6 identical `router.push` targets,
12 haptic calls with the same styles in the same order.

Two findings came out of it:

- **The destructive row buzzed twice.** `Button` fires its own `medium` haptic on
  every press, and the screen added a `Heavy` inside `onPress`. Two impacts in a
  row read as a stutter. Now `hapticStyle="heavy"` and nothing manual — the first
  concrete instance of item 7.
- **`personalSubtitle` was already dead** on `main`, computed and never rendered.

#### PR B — the three bottom sheets

[#810](https://github.com/art-of-v/fuel-voucher-platform/pull/810), merged.
`EditPersonalSheet`, `EditCompanySheet`, `ChangeEmailSheet`.

The constraint that shaped it, **verified rather than assumed**: `BottomSheet` is a
React Native `Modal`, and a `Modal` does not mount its children while `visible` is
false. A throwaway probe confirmed it. So:

- `personalForm` and `companyForm` **stay in the screen** — state owned inside a
  sheet would be discarded on every dismiss, and a user who typed a name, closed
  the sheet and reopened it would find the field empty.
- `showDatePicker` / `tempDate` moved into `EditPersonalSheet` (always false on
  open, nothing to preserve).
- `useChangeEmail` moved into `ChangeEmailSheet`. Its flow is *supposed* to start
  fresh, so the old `changeEmail.reset()` call is **gone rather than relocated** —
  calling it would have reached a different hook instance. The unmount is the reset.

#### Two mistakes worth remembering

- **A JSX comment swallowed the refactor.** A line-splice left a dangling
  `{/* =====`, putting all three components inside a comment. **`tsc --noEmit`
  passed** — a comment eats code silently. Only ESLint's `defined but never used`
  on the three imports exposed it. After a structural edit, lint is the check that
  matters; typecheck alone will not tell you.
- **The global `lucide-react-native` mock was harmful.** It exported a single
  `Icon`, so any component importing a *named* icon got `undefined`, surfacing as
  "Element type is invalid" several frames from the cause. It is now a `Proxy` that
  answers for any name.

`EditPersonalSheet.test.tsx` covers the state-ownership seam directly, including a
case asserting the sheet renders nothing while hidden — the behaviour the whole
design rests on.

### 5. Screen tests for the four largest screens 🔄 In progress

**Why.** Finding 2. The proof is this repo's own history: a tab-bar change
shipped with a **green** test suite while the app rendered **no layout at all**,
because the test mocked `Link` with a plain `cloneElement` instead of the real
Radix `Slot`.

**Done for three of the four.** 44 tests, each suite targeting the screen's own
decisions rather than asserting that it renders:

| Screen | Tests | Covers |
|---|---|---|
| `profile.tsx` | 15 | which edit sheet opens per account type, the worker-context label, ISO birthdate conversion, the auth gate, the delete confirmation |
| `company.tsx` | 18 | the owner-only redirect, fire/recall/block confirmations, the post-gift cleanup, the phone fallback for a half-filled HR import |
| `my-codes.tsx` | 11 | the paid-order delete guard, confirmation before deleting, the deep link that expands an order |
| `map.tsx` | 11 | the CARTO watermark guard, theme-correct tiles, the unknown-fuel label, locate-on-demand |

Every suite was checked for the ability to fail: each rule broken in turn and the
suite re-run. 29 deliberate defects across the four suites, all caught. Three tests
did not survive that check on the first attempt, and in all three the assertion was
at fault rather than the code — a regex that also matched the value it was meant to
exclude, a test that never changed the input it then asserted on, and a theme test
that set no theme so both branches produced the same URL. Those are recorded in the
individual PRs because the pattern is the point: a test that cannot fail is worse
than no test, since it reads as coverage.

**Two things the tests found in the code itself**, both now pinned rather than fixed,
since each is a product call:

- The deep-link effect's comment promises a manual collapse survives, but
  `consumedFocusRef` holds only the most recent id, so A → B → A re-expands A.
- `onDelete` is wired only for pending and renewal orders, and `OrderCard` separately
  gates its swipe behind `needsPayment` — so the paid-order guard in `my-codes.tsx` is
  unreachable from the UI. Two correct layers; the guard is the one that still holds if
  either changes.

**Still to do:** the other 15 screens, and a regression test for the `Link asChild`
behaviour this item exists because of.

Two rules learned the hard way, already written into the architecture doc:
drive real handlers (`responderGrant`, not `fireEvent(el, 'pressIn')`), and mock
the real boundary rather than a convenient one.

**Unblocked by PR A.** Writing the first component test required three shared
stubs, now in `mobile/jest.setup.js`:

| Stub | Why |
|---|---|
| `@react-native-async-storage/async-storage` | native module; reached via `core/ui` → `PageLayout` → `useTheme` → `appStore` |
| `@sentry/react-native` | ESM + native module; reached via `core/ui` → `ErrorBoundary` |
| `lucide-react-native` | ships ESM, outside the Jest transform allow-list. Mocked with a `Proxy` so any named icon resolves — a fixed object leaves every other name `undefined` |
| `react-native-safe-area-context` | `BottomSheet`, `PageLayout` and `Toast` call `useSafeAreaInsets()`, which throws without a provider. Official mock, a default export |

Every screen test will need these, and cannot opt out of them — importing one
`Button` from the `core/ui` barrel pulls in all of them. That is the concrete cost
of the barrel being too broad (item 7), and it is worth pricing in before deciding
how far to narrow it.

#### What "a screen test" should mean

Not "render the screen and assert it does not crash" — expensive, and it proves
little about a 1,100-line file. The question worth asking is **what logic in this
file is not already tested?**

For `map.tsx` the answer was: almost none of it. `rankStations`,
`radarWithinRadius`, `rankBrands`, `bestPriceByStation`, `availableFuels` and
`routeTarget` all live in `lib/` with 258 lines of tests beside 286 lines of code.
Exactly one piece was untested — the search filter, including a Latin-to-Cyrillic
brand alias table (`okko` → `окко`, and three more) that lets a Ukrainian customer
type a brand the way it is actually written. Drop one alias and that network
vanishes from search with no error and no empty-state hint.

Extracted to `lib/search.ts` by
[#814](https://github.com/art-of-v/fuel-voucher-platform/pull/814), merged, and
covered by 25 cases. Two of those cases were wrong on
the first attempt and were caught by running them, not by review: `'солом'`
genuinely **is** a substring of `Солом'янський`, and `Вологодська` contains `воло`,
not `вог`.

`react-native-maps` was verified to import cleanly under Jest, which is what made
any of this possible.

#### The two remaining screens ✅

Both are done, and the honest measure is components, not lines.

`my-codes.tsx` — **1456 → 899**. The voucher card became `VoucherCard`
([#844](https://github.com/art-of-v/fuel-voucher-platform/pull/844)), and the three
context headers — previously JSX *variables* in the function body that merely looked
like components — became `WalletSummaryBar`, `CompanyStockHeader` and
`WorkerFuelHeader` ([#847](https://github.com/art-of-v/fuel-voucher-platform/pull/847)).

`company.tsx` — **1041 → 331**
([#849](https://github.com/art-of-v/fuel-voucher-platform/pull/849), merged). All seven
sections of its return are now components: `CompanyStatsRow`, `InviteWorkerForm`,
`PendingInvites`, `WorkerList`, `IssuedVouchers`, `BlockedVouchers`,
`IssueVoucherModal`.

Every extraction was checked the same way: compare the moved JSX against `main`'s
after stripping comments and running both sides through Prettier, so reflowing cannot
hide a change and a change cannot hide behind reflowing. All ten components came back
verbatim, with their `t()` key sets matching exactly.

Two decisions from #849 that are worth keeping:

- **Shared styles went to `components/styles.ts`, not into each component.** Five of
  the twenty keys are used by five of the seven sections. Copying them per component is
  how two sections end up looking subtly different, and nobody notices until review.
- **Props are `Pick<ReturnType<typeof useCompany>, ...>`**, not hand-written types, so
  they cannot drift from what the hook actually returns.

#### What is left, and what it is not

`map.tsx` (1343 lines, 3 functions) is the last god screen. Its arithmetic is already
in `lib/` with 258 lines of tests beside 286 lines of code, so what remains is the
rendering, not the logic.

`my-codes.tsx` and `company.tsx` are still single functions — the extractions moved
sections out, they did not make the screens coordinators in the sense of small
functions. That was the right unit of work (one extraction per PR, each provable), but
it is worth being plain that "one function" still describes both files.

#### A process trap — now closed, so read the history not the advice

This used to say: *"`prettier --write` on `app/map.tsx` reformatted all 1,100
lines... **97 files in the repo do not match Prettier**, so `format:check` already
fails on `main`."* Both halves were true in October 2026 and both are now false.

Fixed by [#835](https://github.com/art-of-v/fuel-voucher-platform/pull/835), which
formatted the app in one whitespace-only commit, and
[#839](https://github.com/art-of-v/fuel-voucher-platform/pull/839), which put
`npm run format:check` into the mobile CI job where it is required through
`ci-required`. So the failure mode that trap described cannot recur silently.

Two things it did leave behind, both worth knowing:

- **Format the whole file, never a fragment.** Prettier is not a line-range tool.
  Hand-formatting part of a file and leaving the rest produces a file that passes
  `--check` and reads inconsistently.
- **The trap was not only a diff-noise problem — it suppressed every other tool.**
  A CI check that fails on `main` gets ignored, because it cannot be made green.
  That is why #835 exists as its own commit rather than folded into a screen
  refactor: a gate can only be introduced once the tree it guards is clean.

### 6. Haptics into the primitives ⏳

**Why.** Finding 5. `Button`, `IconButton` and `Chip` already own their haptics,
but screens also decide for themselves — 100 call sites across 36 files. Changing
how a press feels means editing many files at once, and there is no single place
that decides it.

Note where they moved, because the count alone hides it: `profile.tsx` went from
17 call sites to **zero**, not because anyone removed them but because item 4 moved
the rows into components — `ManagementSection.tsx` alone now carries 6. Nothing
about haptic policy improved; it got distributed further from the primitives.

Deliberately **not** a global "haptic service". The primitives already model it
correctly; the work is deleting the screen-level calls that duplicate them, and
deciding what remains (destructive actions, selection changes) deserves to be
explicit.

### 7. Finish the architecture migration ⏳

**Why.** Finding 4. One domain, two locations: voucher data in
`features/vouchers/`, voucher UI in `src/components/`. Also gives
`core/ui/index.ts` a reason to shrink — importing `Text` currently drags in
`ErrorBoundary` → Sentry → `expo-constants`.

Lowest urgency of the seven: it is structural tidiness, not a correctness risk,
and it should follow the screens it touches rather than precede them.

---

## Notes for whoever picks this up

- **Work in a worktree**, never the primary checkout. The primary tree usually
  carries unrelated WIP.
- **Rebase once, right before pushing.** Polling `origin/main` while working just
  produces conflicts you then have to resolve twice.
- A `Link asChild` child must receive **one flat style object**. Array and
  function styles are silently discarded — and a mock that uses plain
  `cloneElement` will not reproduce it.
- After any rebase, **recount the numbers in this file and in
  `MOBILE-ARCHITECTURE.md`**. Three of them went stale in a single day, and a
  rebase is not the only thing that moves them — see the two notes below.
- **Run `npm run format` before you commit mobile code.** `format:check` is a
  required CI check since #839, so an unformatted file is a red build rather than
  a review comment. CI runs it after Lint, so it fails fast, but it still costs a
  round trip.
- **Count functions, not lines, when judging a screen.** Line counts here grew
  ~80% with zero statements added when the app was reformatted. A screen's
  top-level function count is what indicates whether it is still one screen.
- Count `as any`, haptic call sites and locale keys by reading the tree, not by
  trusting the numbers above. All three were recounted for this update and all
  three had drifted.