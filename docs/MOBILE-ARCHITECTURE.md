# Mobile architecture

How the code in `mobile/` is organised, and — more usefully — **where new code
goes**. `DESIGN.md` covers how the app _looks_; this covers how it is _built_.

Written for someone joining the project. If you only read one section, read
[Where does my code go?](#where-does-my-code-go).

---

## The two directories

```
mobile/
  app/            routes. One file per screen, expo-router file-based.
  src/
    core/         cross-cutting infrastructure: API client, design system, session, i18n.
    features/     one folder per product area. Owns its data, logic and UI.
    components/   pre-features legacy. See Known debt.
```

`app/` and `src/` split on a single question: **does this file describe a route,
or everything else?** A file in `app/` is a screen. Anything reusable lives in
`src/`.

### Why `core/` and `features/` are separate

`core/` is what every feature needs and no feature owns: the API client, the
design system, session state, i18n, navigation helpers. `features/` is what a
product area owns end to end: its endpoints, its rules, its components.

The practical effect is that deleting a feature folder should delete a feature.
Today `features/stations/` really is separable; `features/savings/` is one file
and could be. That property is the point of the split.

---

## Where does my code go?

The question the codebase gets asked most often. Answer it in this order.

| I am adding…                                  | It goes in                                            | Example                   |
| --------------------------------------------- | ----------------------------------------------------- | ------------------------- |
| A screen                                      | `app/<route>.tsx`, default-exported                   | `app/savings.tsx`         |
| A request to a new endpoint                   | `features/<area>/api/<verb><Noun>.ts`                 | `api/getStations.ts`      |
| A hook that wraps a request                   | `features/<area>/hooks/use<Noun>.ts`                  | `hooks/useStations.ts`    |
| A rule that is true regardless of the network | `features/<area>/lib/<rule>.ts`                       | `lib/eligibility.ts`      |
| UI only that area uses                        | `features/<area>/components/<Thing>.tsx`              | `components/FuelCard.tsx` |
| Something every screen needs                  | `core/ui/<Thing>.tsx`, then export it from the barrel | `core/ui/Card.tsx`        |
| A token                                       | `core/design/layout.ts` or `typography.ts`            | `spacing.lg`              |
| A colour meaning                              | `core/design/themes.ts` — never a hex in a screen     | `status.danger.base`      |
| A translated string                           | all four files in `core/i18n/translations/`           | `renew.pay`               |

Two defaults that are worth stating because they get broken:

- **A new UI primitive goes in `core/ui/` and is exported from
  `core/ui/index.ts`.** Screens import from the barrel and nowhere deeper —
  currently 27 files do it that way and zero reach past it. If a screen needs a
  style no component provides, that is a gap in the system: extend the
  component rather than writing the style in the screen.
- **A `lib/` rule is pure and tested.** It takes values and returns a decision.
  If it needs the network, it is not a `lib/` rule — it is an `api/` call behind
  a `hooks/` wrapper.

---

## Layer rules

The dependency direction is one-way:

```
app/  ──►  features/  ──►  core/
```

`core/` must not import from `features/`, and `features/` must not import from
another feature. `features/` is clean today — no cross-feature imports at all.
`core/` has exactly one exception:

> **`core/notifications/push.ts` imports `features/notifications/api/pushTokensApi`.**
> Token registration needs both the notification plumbing and the endpoint, and
> the endpoint landed in the feature while the plumbing landed in core first. It
> creates a module-level cycle (`core → features → core`). Do not copy it. If you
> are adding a notifications-adjacent call, put it in `features/notifications/`
> and have `core/` call outward only through an event or a callback.

### Screens reaching into `api/`

Four screens call a feature's `api/` file directly instead of going through a
`hooks/` wrapper — `checkout`, `contracts`, `invitations`, `savings`, across
seven import statements. This is tolerated but it is a deviation, not the
pattern: a direct call bypasses the query cache, so it cannot dedupe, cannot
retry, and re-renders on every mount. **New code goes through a hook.** For
mutations with no cache to sit in, the hook still owns the call so the screen
does not.

---

## Data flow

Three files, and `features/stations/` is the reference implementation.

**1. `api/getStations.ts` — one endpoint, one function, no React.**

```ts
import { apiFetch } from '../../../core/api/apiClient';

export async function getStations(): Promise<Station[]> {
  const response = await apiFetch('/api/stations');
  if (!response.ok) throw new Error('Failed to fetch stations');
  return response.json();
}
```

`apiFetch` is the only sanctioned way to reach the network. It carries the bearer
token, refreshes it once on a 401 (single-flight, so concurrent 401s trigger one
refresh), and retries 5xx with exponential backoff.

Two places bypass it with a bare `fetch`, for different reasons:

- `core/utils/versionCheck.ts` calls `/api/app-version` before there is a session,
  so it cannot send a token. There is no comment justifying it, and it also has
  no retry and no timeout.
- `features/auth/hooks/useLogin.ts` probes `/api/auth/device/verify-raw` inside a
  `__DEV__` block. Diagnostic only; never shipped.

Neither is a pattern to copy. If you need an unauthenticated request, add a
named variant to `apiClient` so the choice stays visible in one place.

**2. `hooks/useStations.ts` — react-query, and the decisions that go with it.**

```ts
export function useStations() {
  const isActive = useAppStateActive();
  return useQuery<StationWithFuels[]>({
    queryKey: ['stations'],
    queryFn: async () => {
      /* join two endpoints */
    },
    staleTime: 15_000,
    refetchInterval: isActive ? 30_000 : false,
  });
}
```

The hook owns the query key, the cache policy and the polling. This is where a
product decision like "admin changes prices while the catalog is open" becomes
code, instead of a `setInterval` in a screen.

**3. `app/index.tsx` — the screen.** It calls the hook, reads
`isLoading` / `error` / `data`, and renders `LoadingState`, `ErrorState`,
`EmptyState` or content. It does not fetch.

Shared response shapes live in `core/types/api.ts`. Two endpoints that need a
join — like stations and fuel types — are joined in the `queryFn`, not in the
screen.

---

## State: what lives where

| Kind of state          | Lives in                                      | Example                                            |
| ---------------------- | --------------------------------------------- | -------------------------------------------------- |
| Server data            | react-query, via a hook                       | stations, vouchers, invoices                       |
| Session and device     | `core/state/appStore.ts` (zustand, persisted) | `isAuthenticated`, `theme`, `currentLegalEntityId` |
| Cart                   | `features/cart/store/cartStore.ts`            | basket contents                                    |
| Anything a screen owns | `useState` in that screen                     | form fields, which sheet is open                   |

The line: **if the server owns it, it is react-query; if it survives an app
restart and is about this device or session, it is a persisted store; otherwise
it is local.**

`appStore` is persisted to `AsyncStorage` and is read synchronously at startup,
so it holds facts about the _session_, never about the _server_. If you find
yourself wanting to cache a server list there, that is a react-query cache with
the wrong persistence.

---

## Rules you cannot guess

These cost a day each to learn the hard way. Each one says whether a machine will
catch you breaking it, because that is what you can rely on and what you cannot.

| Rule                                                                                             | Caught by                  |
| ------------------------------------------------------------------------------------------------ | -------------------------- |
| [Polling gated on foreground](#polling-must-be-gated-on-foreground)                              | convention only            |
| [Errors carry a code, not a message](#errors-carry-a-code-not-a-message)                         | convention only            |
| [`t()` returns the key when missing](#t-returns-the-key-when-a-translation-is-missing)           | `parity.test.ts`           |
| [`Link asChild` destroys styles](#link-aschild-destroys-array-and-function-styles)               | `bottom-tabs.test.tsx`     |
| [No hard-coded values in screens](#no-hard-coded-spacing-radius-font-size-or-z-index-in-screens) | `tokens/use-design-tokens` |
| [Screens do not read insets](#screens-do-not-read-safe-area-insets)                              | `DESIGN.md` §11 only       |
| [Bottom padding is derived](#bottom-padding-is-derived)                                          | convention only            |

### Polling must be gated on foreground

```ts
const isActive = useAppStateActive();
return useQuery({ refetchInterval: isActive ? 30_000 : false });
```

React Native has no window-focus concept, so a TanStack Query `refetchInterval`
keeps its timer across a background/foreground cycle and fires on resume. That
races the token-refresh single-flight guard and logs the user out
spuriously — incident #26. Every polling query goes through
`useAppStateActive()`.

### Errors carry a code, not a message

The backend returns a machine code. Convert it at the edge, never show a raw
string:

```ts
throw new VoucherActionError(status, 'invalid_state'); // in api/
Alert.alert(t('renew.errorTitle'), t(renewalErrorKey(code))); // in the screen
```

The pattern is `SomethingError` (a class with `status` and `code`) in `api/`,
and a `somethingErrorKey(error): string` helper next to it that maps to a
translation key. A user-facing error is always `t(...)` of a code. If you are
about to `Alert.alert(err.message)`, stop — that string is for developers.

### `t()` returns the key when a translation is missing

Not an error, not a warning — it renders `renew.pay` on a button. That is why
`core/i18n/translations/parity.test.ts` exists and why all four locale files must
be edited together.

### `Link asChild` destroys array and function styles

```ts
<Link asChild>
  <Pressable style={({ pressed }) => [...]} />
</Link>
```

renders with **no style at all**. expo-router renders the child through Radix's
`Slot`, whose `mergeProps` joins styles with `{ ...slotStyle, ...childStyle }` — a
spread. A function spreads to `{}`; an array spreads to `{ 0: …, 1: … }`, and
Yoga reads neither. Pass one flat object. See the comment at the top of
`components/bottom-tabs.tsx`.

### No hard-coded spacing, radius, font size or z-index in screens

`tokens/use-design-tokens` enforces this on new screens. Where a number has a
token, the error names it (`tokens.spacing.4xl`); where it does not, the error
says so and asks you to either extend the scale or comment the exception — it
will not tell you to write a token that does not exist. Sixteen older screens
are exempt and listed in `eslint.config.js`; deleting a line from that list is
the whole migration step for that file.

### Screens do not read safe-area insets

`PageLayout` wraps every screen and owns insets. `useSafeAreaInsets()` inside a
screen adds the notch a second time. The only components allowed to read insets
are the ones outside a `PageLayout` tree: the tab bar and the overlays. If
content is clipped, the chrome measurement in `core/design/layout.ts` is wrong —
fix it there.

### Bottom padding is derived

`useContentInsets()` returns the space to reserve. Do not add to the result.

---

## Tests

Tests sit next to the code as `<name>.test.ts`, and they run under `npm test`.

| Testing this            | Put the test in           | Example                           |
| ----------------------- | ------------------------- | --------------------------------- |
| A pure rule             | next to the rule          | `lib/eligibility.test.ts`         |
| A hook                  | next to the hook          | `hooks/useMyCodes.test.ts`        |
| Env/config resolution   | next to it                | `observability/sentry.test.ts`    |
| Translations            | once, over all four files | `translations/parity.test.ts`     |
| A component's behaviour | next to the component     | `components/bottom-tabs.test.tsx` |

Two things that matter when writing one:

- **Drive real handlers.** For `Pressable`, `fireEvent(el, 'pressIn')` finds the
  `onPressIn` prop and calls it directly, skipping `Pressability`, so the
  `pressed` state behind a style function never flips and the press goes
  untested. Use `responderGrant` / `responderRelease` — the path a finger takes.
- **Mock the real boundary, not a convenient one.** A `Link` mock built on
  `cloneElement` reported the tab bar as fully laid out while the app drew no
  layout, because the real `Link` goes through Radix's `Slot`. Mock through the
  real module whenever the wrapper does anything.

---

## Known debt

Stated plainly so nobody is surprised, and so the next change does not make it
worse. Counts are from October 2026.

| Debt                            | Size                                                                           | Where             |
| ------------------------------- | ------------------------------------------------------------------------------ | ----------------- |
| God screens                     | `map.tsx` 1097 lines, `profile.tsx` 863, `my-codes.tsx` 752, `company.tsx` 723 | `app/`            |
| No screen tests                 | 0 tests across 19 routes                                                       | `app/`            |
| Hard-coded style values         | 530, in 16 exempt screens                                                      | `app/`            |
| ~~Domain split across two folders~~ | resolved: voucher UI and data both in `features/vouchers/`           | -                 |
| Haptic policy in screens        | 110 call sites, 17 of them in `profile.tsx`                                    | `app/`            |
| Legacy flat folder              | `OrderCard.tsx` 645 lines, `VoucherDetailModal.tsx` 719                        | `features/vouchers/components/` |

The god screens are the root problem: a file that long cannot be searched,
cannot be reviewed in one pass, and cannot be tested. `profile.tsx` is the
clearest case — one function of ~840 lines with nine pieces of state. When you
touch one of these, extracting a section into its own file is part of the task,
not a follow-up.

The exemption list in `eslint.config.js` is the tracker: it only shrinks when
someone cleans a file.
