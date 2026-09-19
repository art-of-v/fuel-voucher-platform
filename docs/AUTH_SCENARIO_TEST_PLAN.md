# FuelFlow Auth — Scenario Test Plan (prod, real device)

End-to-end manual verification of the passwordless-auth spec (Scenarios A–M + acceptance
criteria) against **production** (`api.palne.shop`, Hetzner host `fuel-flow-1`), driven from a
real device with a clean database.

> This complements `MANUAL_TESTING.md` (which is dev/`DevBypass` oriented). This file is the
> **production** run: no bypass code, real SMS/email, server-side verification over SSH.

---

## Environment & prerequisites

| Thing | Value / how |
|---|---|
| API base | `https://api.palne.shop` (mobile `app.json` already points here) |
| Admin panel | same origin, `https://app.palne.shop` (nginx proxies `/api`) |
| DB access | `ssh root@palne.shop` → `docker exec -it fuelflow-postgres bash -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'` |
| Logs (Loki) | no public port; `docker exec fuelflow-loki wget -qO- "http://localhost:3100/loki/api/v1/query_range?..."` or Grafana → Explore |
| Loki labels | `service="fuelflow-api"`, `environment="Production"` (**case-sensitive**) |
| Audit table | Postgres `provider_event_outbox` (`event_type`, `summary`, `changed_at_utc`) |
| OTP code | **No `000000` on prod** (DevBypass force-disabled). Real SMS, or email for staff-with-email. |
| Rate limits | send-code 3/min/phone; verify 5/5min/phone; OTP ceiling 12/10min/IP; global 300/min/IP |
| Test account | `+380677757766` (registered as User, promoted to ProductOwner) |

### First Staff user (clean DB has none — only role rows are seeded)
1. Register the phone normally in the app (creates a `User`-role row, `is_active=false`).
2. Promote via SQL (no restart):
   ```sql
   UPDATE users SET role_id = '2c4a8b1e-5f3d-4a7e-9c1b-8d2e6f4a3b5c', updated_at_utc = NOW()
   WHERE phone_number = '+380677757766';   -- ProductOwner
   ```
   Role GUIDs: ProductOwner `2c4a8b1e-…-3b5c` · Admin `0b6c503a-…-b474bd` · Manager `3d5b9c2f-…-6d8e` · User `1b445bd0-…-142f`
3. Re-login so the new token carries the role claim.

---

## The 3-layer verification method (reused per scenario)

1. **Device** — what the app/panel shows.
2. **DB** — the row state that proves it server-side (e.g. `is_active`, `is_used`, `is_revoked`, `token_version`, absence of a `verification_codes` row).
3. **Loki** — the log line / audit event.

Reusable Loki query (last 1h, a message substring):
```
{service="fuelflow-api", environment="Production"} |= "SUBSTRING"
```

---

## Scenarios → acceptance criteria

Legend: ☐ todo · ☑ passed · ⚠ finding

### Phase 0 — Bootstrap
- ☑ **A** New user registers by phone only → `is_active=false`, User role. *(AC: register by phone; new users inactive)*
- ☑ **H** Staff without email → SMS code; login succeeds. *(AC: staff-no-email → SMS)*

### Phase 1 — Code & registration rules
- ☐ Verification code single-use (replay → 401). *(AC: single-use)*
- ☐ Second code invalidates the first (supersession; only newest verifies). *(AC: no 2nd active code)*
- ☐ Code expires after 5 min (force `expires_at_utc` past → 401). *(AC: 5-min TTL)*
- ☐ 5 wrong guesses burns the code (`failed_attempts`→5, `is_used=t`). *(AC: brute-force cap)*
- ☐ Rate limiting on send + verify (`429`). *(AC: rate-limit)*

### Phase 2 — Sessions & devices
- ☐ **C** New device → new independent session; Device A stays logged in. *(AC: independent sessions)*
- ☐ **B** Reopen app, session valid → Face ID, no SMS. *(AC: reopen without re-auth)*
- ☐ **E** Explicit logout revokes only that device + disables Face ID; others unaffected. *(AC: logout scope; Face ID off)*
- ☐ **D** 14-day inactivity expiry (force refresh-token `expires_at_utc` past → refresh 401). *(AC: 14-day inactivity)*
- ☐ Reinstall → new device/session (no restore). *(AC: reinstall re-auth)*

### Phase 3 — Face ID rules
- ☐ Works only on the enrolled device; never creates a session on a new device / after reinstall / after logout. *(AC: Face ID device-local)*

### Phase 4 — Statuses & purchase
- ☐ Inactive user: full app access **except** purchase (403 on checkout). *(AC: inactive can't purchase)*
- ☐ **K** Staff activates → `is_active=true` → purchase allowed, no re-login. *(AC: activation, no logout)*
- ☐ **L** Ban → all sessions revoked immediately (401 on next call, every device). *(AC: ban revokes all)*
- ☐ **M** Unban → old sessions stay dead, must re-auth. *(AC: unban no restore)*

### Phase 5 — Staff / authz
- ☑ **F** Non-staff admin login → rejected before any code sent (silent, generic, masked Loki warning). *(AC: non-staff rejected pre-code)*
- ☐ **G** Staff **with** email → email code. *(AC: staff-email → email)*
- ☐ **I** Promote User→Staff → existing session unchanged; new login gets staff. *(AC: promotion doesn't upgrade live session)*
- ☐ **J** Remove staff role → all sessions revoked immediately; next login uses current authz. *(AC: staff-removal revokes all)*
- ☐ Remove a staff user's email → next auth falls back to SMS. *(AC: email removed → SMS)*

> Note: role change (I/J) and editing another user's email have **no admin-panel UI** — drive via the
> `POST /api/admin/users/{id}/role` endpoint (ProductOwner token) or SQL. Activate/Ban/Unban have buttons.

---

## Findings log (see memory `auth-live-test-findings.md`)

- ⚠ **Successful staff logins under-audited.** `AdminLoggedIn` audit fires only for literal
  `Role.Name == "Admin"` (`VerifyCodeCommand.cs:216`); a **ProductOwner** login produced **0** rows
  in `provider_event_outbox`. The *failed*-login auditor checks all three staff roles, so the
  asymmetry is a bug. Fix: gate on `SeedRoles.IsStaff`, not a literal. *(User: log & fix after the run.)*
- ⚠ **Staff roles hardcoded** to `ProductOwner/Admin/Manager` in several places (send-code gate,
  `[Authorize]`, SPA) — a brand-new staff role name isn't recognized without code edits, vs the
  spec's "additional roles later" requirement. Data model (by level) is fine; the literals aren't.

## Decisions captured
- **Silent reject** for non-staff admin login is intentional (spec §13 anti-enumeration). If clearer
  UX is wanted, only a **uniform** "if authorized, a code was sent" message is safe.
