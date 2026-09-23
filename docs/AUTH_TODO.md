# Auth — Findings & TODO (from the prod manual test run)

Consolidated action list from the live passwordless-auth test run against **production**
(palne.shop) on 2026-09-19 → 2026-09-22. Companion to `AUTH_SCENARIO_TEST_PLAN.md` (the scenario
map) — this file is the **actionable** list: bugs to fix, features to build, tests still to run.

Legend: 🔴 bug · 🛠️ feature · 🧪 test · ✅ done · ⏳ pending

> **Status 2026-09-22 — all bugs fixed and both features shipped (merged to `main`).**
> Bugs: #601 (reinstall), #602 (audit), #603 (429), #606 (hardcoded roles, folded into the role
> matrix). Features: #606 (role matrix + PO singleton), #607 (admin verified email), plus #610
> (self-service verified email — the follow-up). The only items left are the two-device **live**
> confirmations of Scenarios I and J.
>
> **Status 2026-09-23 — Scenario J live confirmation FAILED and is now fixed (this PR).** Demoting a
> live staff account did not revoke its sessions: the demotion threw (HTTP 400) but the role change
> had already committed, and the admin's retry saw `oldRole == newRole` → `isDemotion=false` and
> skipped revocation, leaving the demoted user minting old-role tokens for the full refresh TTL. Root
> cause + fix in bug #7 below.

---

## 🔴 Bugs to fix

1. ✅ **(#602)** **Staff-login audit fires only for literal `"Admin"`.** `VerifyCodeCommand.cs` gates the
   `AdminLoggedIn` audit event on `user.Role?.Name == "Admin"`, so **ProductOwner** and **Manager**
   logins produce **no** audit record (verified live: 0 rows in `provider_event_outbox` after a
   successful PO login). The *failed*-login auditor already checks all three staff roles — the
   asymmetry is the bug. **Fix:** fire the audit for any staff role (`SeedRoles.IsStaff` / level
   check, not a literal); consider distinguishing PO/Manager in the event name.

2. ✅ **(#606)** **Staff roles hardcoded** (`ProductOwner`/`Admin`/`Manager` literals) in several places
   (send-code gate, `[Authorize]`, SPA). A new staff role name isn't recognized without code edits.
   **Fix:** drive off the role level / `SeedRoles.IsStaff`, not string literals.

3. ✅ **(#603)** **App shows HTTP 429 as a generic "network error".** Verified live: repeated `send-code` → 429
   while the device only said "network error", with no "too many attempts, wait a minute" guidance.
   Server limiter is correct; this is client error-handling. **Fix (mobile):** map 429 to a specific
   "too many attempts, try again in a minute" message, ideally honoring `Retry-After`, distinct from
   transport/offline errors.

4. ✅ **(#601)** **🔥 Reinstall does NOT clear the session (severe).** Verified live: revoked all server sessions,
   deleted + reinstalled the app → it went straight to the home screen (Face ID), and once the
   15-min access pass expired the app **silently renewed** with the Keychain-persisted refresh token
   (`refresh → 200`, token rotated) and stayed logged in. So **deleting + reinstalling the app never
   forces re-auth** — the refresh token survives in the iOS Keychain. Violates the spec's
   "reinstall → re-authenticate, no restore" and is a device-handover / lost-device risk.
   **Fix (mobile):** on first launch of a fresh install, wipe/ignore auth Keychain items (e.g. a
   "first run since install" flag in non-Keychain storage) so a real login is forced.

7. ✅ **(this PR)** **🔥 Demotion does not revoke the demoted user's sessions (severe, Scenario J).**
   Found in the live two-device run: demoting a staff user (Manager→User) via the admin role dropdown
   returned HTTP 400 ("The request could not be processed") and left every session live. Root cause
   is the interaction of the context's global **NoTracking** default with two chained handlers:
   `SetUserRoleCommandHandler` loaded the target `.AsTracking()` to flip the role, then delegated to
   `LogoutEverywhereCommandHandler`, which **re-fetched the same user untracked** and called
   `Update()` — an EF **identity-map conflict** (`InvalidOperationException`). The role flip had
   already been persisted in its **own** `SaveChanges` **before** the revoke ran, so it committed
   while the revoke threw; the admin's retry then saw `oldRole == newRole`, computed
   `isDemotion=false`, and **skipped revocation entirely** — the demoted user kept minting old-role
   access tokens (via `RoleNameAtIssue`) for the full 14-day refresh TTL. A mocked unit test cannot
   reproduce an EF identity-map conflict, which is why the existing handler unit test stayed green.
   **Fix:** (a) `LogoutEverywhereCommandHandler` loads the user `.AsTracking()` (returns the
   already-tracked instance, no second identity) and drops the redundant `.Update()`; (b)
   `SetUserRoleCommandHandler` wraps the role change + revoke in **one transaction**, so any failure
   rolls the role change back and the retry is a real demotion again. Covered by a new Testcontainers
   regression test (`AdminRoleDemotionIntegrationTests`) that resolves the real handler from DI
   against real Postgres — demotion revokes (→ 401 on the old refresh token), promotion does not.

---

## 🛠️ Features to build

5. ✅ **(#607, self-service #610)** **Admin-panel button to edit a user's email.** Shipped as a
   **verified double-opt-in** change: a confirmation link is emailed to the new address and the
   email only changes once it is opened (`GET /api/auth/email/confirm`); the old address is notified.
   #610 routed the mobile **self-service** profile email change through the same verified flow
   (previously `UpdateUser` wrote it unverified). Editing email still switches OTP delivery: staff
   **with** email → email code, **without** → SMS.

6. ✅ **(#606)** **Role / lifecycle authorization matrix** (fully specced 2026-09-22). Enforce server-side
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
  second, non-PO account* (e.g. the User account +380970011771). Code is implemented + unit-tested
  (#606, `RoleNameAtIssue` snapshot); the promotion-must-not-revoke boundary is now also covered by
  `AdminRoleDemotionIntegrationTests` (this PR). Live two-device confirmation still pending.
- ✅ **Scenario J** — remove a staff role; all that user's sessions revoked immediately. Live run
  **found this broken** (bug #7 above) — the demotion committed the role change but threw before
  revoking, so sessions survived. **Fixed in this PR** and pinned by a Testcontainers regression test
  (`AdminRoleDemotionIntegrationTests`: demotion revokes every refresh token + device, bumps
  `TokenVersion`, and the pre-demotion refresh token is rejected with 401). Live two-device re-confirm
  recommended once deployed.
- ✅ **Remove staff email → OTP falls back to SMS.** Confirmed live 2026-09-22 (cleared email →
  `SMS sent successfully`, no email-code path), then email restored.

---

## ✅ Passed (live, unless noted)

- Phase 0: **A** (register by phone → inactive/User), **H** (staff-no-email → SMS)
- Phase 1: single-use, supersession, 5-min expiry, brute-force cap, rate-limit 429 (automated
  coverage; 429 also confirmed live)
- Phase 2: **B** (reopen → Face ID, no SMS), **C** (new device = independent session), **D** (14-day
  inactivity → forced re-login), **E** (logout is device-scoped + disables that device's Face ID);
  reinstall was ❌ (bug #4) — **now fixed in #601** (fresh install wipes the auth Keychain)
- Phase 3: Face-ID rules — enrolled-device ✅, after-logout ✅; after-reinstall was ❌ (= bug #4,
  **fixed #601**); new-device low-risk / implicitly covered
- Phase 4: inactive-can't-buy ✅, **K** (activate → buy, no re-login) ✅, **L** (ban revokes all
  sessions instantly) ✅, **M** (unban → must re-login, no restore) ✅
- Phase 5: **F** (non-staff admin login silently rejected), **G** (staff-with-email → email code)
