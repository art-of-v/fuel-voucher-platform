# QA Test Access (App Store review sign-in)

A controlled, server-side mechanism that lets a single dedicated QA account sign in to the
**production** mobile app for Apple App Store review, without SMS delivery. It is **not** a general
authentication bypass: it is scoped to one pre-seeded, least-privilege identity and is off unless an
administrator explicitly turns it on.

> This replaces the removed `AUTH_TEST_PHONES` allowlist (commit `7bd78ed`), which was a permanent,
> non-rotating, never-expiring OTP baked into config with no admin kill-switch — effectively a
> standing production credential. The redesign keeps the legitimate QA use case while removing that
> weakness.

## What it does

When **both** of the following hold, the QA phone can sign in with the QA code through the normal
`/api/auth/send-code` → `/api/auth/verify` flow (no SMS is sent, no provider cost):

1. A QA code is configured on the server (`Auth__QaTestAccessCode`), and
2. An admin has turned **QA Test Access = ON** in the admin panel.

The QA identity:

- **Phone:** `+380110010203` (an identifier, not a secret — the design assumes it may become known).
- **Account:** a seeded `User` row flagged `is_qa_account = true`, with the least-privilege **User**
  role. It is `is_active = true` so a reviewer can exercise the full workflow (including a test
  purchase), but has no staff/admin permissions.
- **Code:** a secret supplied only via configuration, hashed at boot (SHA-256). The plaintext is
  never stored, logged, or returned by any API.

## Why it exists

Apple App Store review needs to sign in to the production build without receiving a real SMS to a
Ukrainian number. This provides that in a way that is auditable, reversible, and safe to leave in the
codebase between review cycles (disabled by default).

## How to enable / disable

**Enable (during QA):**
1. Ensure `AUTH_QA_TEST_ACCESS_CODE` is set in the deploy environment (see below). If it is blank the
   switch does nothing — the mechanism stays disabled (fail-safe).
2. In the admin panel: **Settings → QA test access → toggle ON**.

**Disable (after QA approval):**
1. In the admin panel: **Settings → QA test access → toggle OFF**.
2. This takes effect immediately: new QA sign-ins stop working **and** any live QA session is
   revoked (see below). No mobile or backend release is required to toggle in either direction.

## Who is authorized

- **Changing the switch** requires the `QaTestAccessAdmin` policy — **Admin** or **ProductOwner**
  only. Managers (who can otherwise manage users) deliberately cannot toggle an authentication
  mechanism.
- **Viewing status** is allowed for any Staff member (read-only).
- Every change is recorded in the audit outbox (`ProviderEventOutbox`, aggregate `QaTestAccess`) with
  the acting admin's identity, the previous and new value, and a timestamp. The QA code and phone are
  never written to the audit trail.

## Behavior when disabled

- The QA phone behaves like any other unknown phone — there is no API difference that reveals it is a
  QA account (no enumeration oracle).
- The QA code is never issued and, even if one was issued moments before the switch flipped, the
  verify path re-checks the switch and rejects it with the same generic "invalid or expired" error.

## What happens to existing QA sessions when disabled

Disabling **immediately revokes** the QA account's sessions:

- The QA user's `TokenVersion` is bumped, which invalidates every outstanding access token on the
  next request (enforced by `SessionValidationMiddleware`).
- All the QA account's refresh tokens are revoked and its active devices are marked revoked, so no
  new access token can be minted at `/api/auth/refresh`.
- Any unredeemed QA verification codes are burned.

This mirrors the existing ban/demotion revocation behavior, so "OFF" is immediate and total, not
"valid until expiry".

## Fail-safe guarantees

QA access defaults to **disabled** whenever anything is missing or wrong:

- No `Auth__QaTestAccessCode` configured → disabled (the switch has no effect).
- `QaTestAccess:Enabled` setting missing, malformed, or unreadable (DB error) → treated as disabled.
- Normal customer authentication is unaffected in all of these cases.

The switch state is read fresh from the `app_settings` table on every attempt (no cache), so turning
it off is effective on the very next request.

## Configuration reference

| Where | Key | Meaning |
|---|---|---|
| Deploy secret / env | `Auth__QaTestAccessCode` (compose: `AUTH_QA_TEST_ACCESS_CODE`) | Plaintext QA code, hashed at boot. Blank ⇒ mechanism permanently off. |
| Runtime setting (DB `app_settings`) | `QaTestAccess:Enabled` | `true`/`false`. Managed via the admin panel; default/fail-safe `false`. |

Rotate the code by changing `AUTH_QA_TEST_ACCESS_CODE` and restarting the API.

## How QA uses it

1. Admin sets the code in the environment (once) and turns **QA Test Access = ON**.
2. On the production app's login screen, the reviewer enters `+380110010203`, taps to get a code,
   then enters the QA code (communicated out-of-band, never shipped in the binary).
3. They complete the normal device-registration/challenge flow and use the app.
4. After approval, the admin turns **QA Test Access = OFF**. The QA session is revoked.
5. Next cycle: turn it ON again — no release required.

## How developers test locally

- **Unit tests:** `backend/tests/FuelFlow.UnitTests/Auth/QaTestAccessTests.cs` cover the send/verify
  paths, the switch, revocation, audit, and the security boundaries (arbitrary phone cannot use the
  QA code; QA code cannot authenticate another account; per-code rate limiting).
- **Integration tests:** `backend/tests/FuelFlow.IntegrationTests/QaTestAccessIntegrationTests.cs`
  drive the full HTTP flow against real Postgres, including migration seeding and end-to-end session
  revocation on disable. They inject a throwaway QA code hash via `PostConfigure<AuthOptions>` — the
  real code is never embedded in source or tests.
- **Manually:** set `Auth__QaTestAccessCode` in your local environment, run the API, enable the
  switch via the admin panel (or `PUT /api/admin/qa-test-access` as an Admin/ProductOwner), and sign
  in as the QA phone with your code.

## Handling during future releases

- The feature is safe to keep in production **disabled**. Leave `AUTH_QA_TEST_ACCESS_CODE` blank
  outside review windows for defense in depth (then even the admin switch cannot enable it).
- For a new review cycle: set the code, flip the switch ON, hand the reviewer the credentials, and
  flip it OFF (and ideally clear the code) afterwards.

## Threat model note

The QA phone number and the existence of this mechanism may become public; security does not depend
on their secrecy. The real control is the server-side enable/disable state plus the hashed QA code.
The mechanism can only ever authenticate the one seeded QA identity, never an arbitrary account.
