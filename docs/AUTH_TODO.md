# Auth — Findings & TODO (from the prod manual test run)

Consolidated action list from the live passwordless-auth test run against **production**
(palne.shop) on 2026-09-19 → 2026-09-22. Companion to `AUTH_SCENARIO_TEST_PLAN.md` (the scenario
map) — this file is the **actionable** list: bugs to fix, features to build, tests still to run.

Legend: 🔴 bug · 🛠️ feature · 🧪 test · ✅ done · ⏳ pending

---

## 🔴 Bugs to fix

1. **Staff-login audit fires only for literal `"Admin"`.** `VerifyCodeCommand.cs` gates the
   `AdminLoggedIn` audit event on `user.Role?.Name == "Admin"`, so **ProductOwner** and **Manager**
   logins produce **no** audit record (verified live: 0 rows in `provider_event_outbox` after a
   successful PO login). The *failed*-login auditor already checks all three staff roles — the
   asymmetry is the bug. **Fix:** fire the audit for any staff role (`SeedRoles.IsStaff` / level
   check, not a literal); consider distinguishing PO/Manager in the event name.

2. **Staff roles hardcoded** (`ProductOwner`/`Admin`/`Manager` literals) in several places
   (send-code gate, `[Authorize]`, SPA). A new staff role name isn't recognized without code edits.
   **Fix:** drive off the role level / `SeedRoles.IsStaff`, not string literals.

3. **App shows HTTP 429 as a generic "network error".** Verified live: repeated `send-code` → 429
   while the device only said "network error", with no "too many attempts, wait a minute" guidance.
   Server limiter is correct; this is client error-handling. **Fix (mobile):** map 429 to a specific
   "too many attempts, try again in a minute" message, ideally honoring `Retry-After`, distinct from
   transport/offline errors.

4. **🔥 Reinstall does NOT clear the session (severe).** Verified live: revoked all server sessions,
   deleted + reinstalled the app → it went straight to the home screen (Face ID), and once the
   15-min access pass expired the app **silently renewed** with the Keychain-persisted refresh token
   (`refresh → 200`, token rotated) and stayed logged in. So **deleting + reinstalling the app never
   forces re-auth** — the refresh token survives in the iOS Keychain. Violates the spec's
   "reinstall → re-authenticate, no restore" and is a device-handover / lost-device risk.
   **Fix (mobile):** on first launch of a fresh install, wipe/ignore auth Keychain items (e.g. a
   "first run since install" flag in non-Keychain storage) so a real login is forced.

---

## 🛠️ Features to build

5. **Admin-panel button to edit a user's email.** Today there's no UI to edit another user's email
   (API/SQL only). Editing email also switches OTP delivery: staff **with** email → email code,
   **without** → SMS.

6. **Role / lifecycle authorization matrix** (fully specced 2026-09-22). Enforce server-side
   (never trust the SPA); reuse/extend `RoleHierarchy.cs`, `SetUserRoleCommandHandler`,
   `SetUserBannedCommandHandler`, `SetUserActiveCommandHandler`. All admin mutations must use
   `.AsTracking()` (NoTracking default silently no-ops writes).

   **Lifecycle actions (activate / deactivate / ban / unban):**

   | Actor | User | Manager | Admin | ProductOwner |
   |---|---|---|---|---|
   | **Manager** | activate / deactivate / ban / unban | ✗ | ✗ | ✗ |
   | **Admin** | ban / unban / activate / deactivate | ban / unban / activate / deactivate | ✗ | ✗ |
   | **ProductOwner** | all | all | all | — |

   **Role changes:** only **Admin** and **PO** can change roles. Admin & PO can set a user's role to
   User / Manager / Admin. **Nobody** can change a **PO's** role.

   **ProductOwner is a singleton** — exactly one (Artem Vashchuk). The PO can do anything to anyone
   below; **nobody can do anything to the PO** (no ban/unban/activate/deactivate/role-change). The
   **PO role is not assignable** — no one can be promoted to PO. Managers act on **Users only** and
   cannot change roles. Admins **cannot touch a PO at all**.

---

## 🧪 Tests still to run

- 🧪 **Scenario I** — promote User→Staff; existing session stays non-staff until re-login. *Needs a
  second, non-PO account* (e.g. the User account +380970011771).
- 🧪 **Scenario J** — remove a staff role; all that user's sessions revoked immediately. *Needs a
  second staff account (promote first, then strip).* 
- 🧪 **Remove staff email → OTP falls back to SMS.** Solo-testable on the PO account (staff-with-email
  → drop email → next login via SMS; restore email after).

---

## ✅ Passed (live, unless noted)

- Phase 0: **A** (register by phone → inactive/User), **H** (staff-no-email → SMS)
- Phase 1: single-use, supersession, 5-min expiry, brute-force cap, rate-limit 429 (automated
  coverage; 429 also confirmed live)
- Phase 2: **B** (reopen → Face ID, no SMS), **C** (new device = independent session), **D** (14-day
  inactivity → forced re-login), **E** (logout is device-scoped + disables that device's Face ID);
  **reinstall → ❌ FAILS**, see bug #4
- Phase 3: Face-ID rules — enrolled-device ✅, after-logout ✅; after-reinstall ❌ (= bug #4);
  new-device low-risk / implicitly covered
- Phase 4: inactive-can't-buy ✅, **K** (activate → buy, no re-login) ✅, **L** (ban revokes all
  sessions instantly) ✅, **M** (unban → must re-login, no restore) ✅
- Phase 5: **F** (non-staff admin login silently rejected), **G** (staff-with-email → email code)
