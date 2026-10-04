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
| Tests are next to code | 20 suites, `npm test` green, located beside what they test |
| Locales are in sync | 444 keys, identical across en/uk/de/es |

### What needs fixing

| # | Finding | Size | Severity |
|---|---|---|---|
| 1 | God screens — one function holding an entire screen | `map.tsx` 1097, `profile.tsx` 863, `my-codes.tsx` 752, `company.tsx` 723 | **Critical** |
| 2 | No screen tests at all | 0 tests across 19 routes | **Critical** |
| 3 | Design system ignored by screens | 530 hard-coded values vs 41 `tokens.*` uses | **High** |
| 4 | Architecture half-migrated — voucher UI in `src/components/`, its data in `features/vouchers/` | 3 files, 1333 lines | Medium |
| 5 | Haptic policy lives in screens | 110 call sites, 17 of them in `profile.tsx` | Medium |
| 6 | No architecture document | — | Medium |

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

**Done** — [#796](https://github.com/art-of-v/fuel-voucher-platform/pull/796), open.

`MOBILE-ARCHITECTURE.md`, organised around "where does my code go?". Includes a
"rules you cannot guess" section — polling gated on `useAppStateActive()`, error
codes not messages, `Link asChild` discarding styles — each stating whether a
machine catches you breaking it. Only two of the seven are automated.

Three claims in the first draft were false and are corrected: `fetch` **is**
called outside `core/api` in two places, four screens (not seven) bypass the
hooks layer, and `core/` **does** import `features/` in one spot.

### 4. Split `profile.tsx` 🔄 In progress

**Why.** The clearest case of finding 1: one function of ~840 lines with nine
pieces of state. It cannot be searched, reviewed in one pass, or tested.

**Plan: two PRs, lowest risk first.**

- **PR A — the five body sections.** Pure presentation plus a few callbacks. No
  state moves. Removes ~285 lines from the screen.
- **PR B — the three bottom sheets.** These need care: `BottomSheet` is a React
  Native `Modal`, and it does **not** mount its children while `visible` is
  false. So moving form state into a sheet would reset unsaved edits between
  open/close — a user-visible behaviour change. The form state stays in the
  screen and is passed down; only `showDatePicker` and `tempDate`, which are
  always false on open, may move.

#### Work in flight

Branch `refactor/split-profile-screen`, worktree `C:/tmp/ff-prof`, based on
`fed493b6`. **Nothing committed.** Current state:

- Five section components written to `mobile/src/features/profile/components/`:
  `ProfileHeaderCard.tsx` (111), `ManagementSection.tsx` (137),
  `ActivitySection.tsx` (85), `PreferencesSection.tsx` (67),
  `AccountActions.tsx` (47).
- `app/profile.tsx` rewritten as a list of sections, 916 → 623 lines.
- **It does not compile yet.** Three things left to do:
  1. remove the imports the screen no longer uses — `ThemeType`, `SectionHeader`,
     `ListItem`, `Badge`, `Select`, `Linking`, and 11 icons
     (`Bell`, `Building2`, `FileSignature`, `FileText`, `PiggyBank`, `Mail`,
     `LogOut`, `Trash2`, `Shield`, `Briefcase`, `Layers`, `ArrowLeftRight`)
  2. delete `languageOptions` and `themeSelectOptions`, now owned by
     `PreferencesSection`
  3. add the five component imports

Then: `npm run typecheck`, `npm run lint`, `npm test`, and add a component test
for at least one extracted section before opening the PR.

### 5. Screen tests for the four largest screens ⏳

**Why.** Finding 2. The proof is this repo's own history: a tab-bar change
shipped with a **green** test suite while the app rendered **no layout at all**,
because the test mocked `Link` with a plain `cloneElement` instead of the real
Radix `Slot`.

**Do it after item 4** for `profile.tsx` — splitting it first is what makes it
testable. `map.tsx`, `my-codes.tsx` and `company.tsx` can be tested as-is.

Two rules learned the hard way, already written into the architecture doc:
drive real handlers (`responderGrant`, not `fireEvent(el, 'pressIn')`), and mock
the real boundary rather than a convenient one.

### 6. Haptics into the primitives ⏳

**Why.** Finding 5. `Button`, `IconButton` and `Chip` already own their haptics,
but screens also decide for themselves — 110 call sites. Changing how a press
feels means editing 14 files, and there is no single place that decides it.

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
  `MOBILE-ARCHITECTURE.md`**. Three of them went stale in a single day.