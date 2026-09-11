# FuelFlow Security Guide

## Simple Guide

FuelFlow uses a **Magic Safe and a Secret Stamp** approach to security.

### The Magic Safe (Secure Enclave)
Inside your phone, there is a tiny, invisible safe. It is so strong that even the phone's owner can't peek inside. When you first join FuelFlow, your phone goes into this safe and creates two items:
1. **The Private Key**: This stays locked in the safe forever.
2. **The Public Key**: A lock given to the FuelFlow server.

### The Secret Question (The Challenge)
Every time you open the app, the server sends your phone a random riddle (a "Challenge"). The riddle is different every time.

### The Secret Stamp (Digital Signature)
To answer the riddle, your phone uses FaceID or Fingerprint to "stamp" the answer with your Private Key. This stamp is unique to your key and breaks if anyone tampers with the request.

### The Server's Check
The server verifies the stamp with your Public Key. If it fits, you're authenticated. If not, access is denied.

### Why is this better?
- **Stolen token is useless** — every request needs a fresh stamp from your physical phone.
- **No passwords to leak** — only public keys are stored server-side.

---

## Implemented authentication model

This section describes what the backend **actually does today** (not an aspirational design).
Cross-reference [Fraud analysis findings](#fraud-analysis-findings) below for money-integrity controls.

### Login (phone OTP)

1. `POST /api/auth/send-code` — the server generates a 6-digit code. With **`Auth:DevBypass=true`**
   the code is the fixed `000000` and SMS is faked (`FakeSmsService`); with the flag off it
   generates a random code and sends it via Twilio. The bypass is a single explicit flag,
   **decoupled from `ASPNETCORE_ENVIRONMENT`** (see rationale below). Send-code and verify are
   rate-limited.
2. `POST /api/auth/verify` — validates the code (max 5 failed attempts before lockout),
   auto-creates the user for a new phone, rejects deactivated users, and returns a JWT
   **access** token plus a rotating **refresh** token. The refresh token is also set as an
   `HttpOnly`, `Secure`, `SameSite=None` cookie scoped to `/api/auth/refresh`.
3. `POST /api/auth/refresh` — rotates the pair. Refresh tokens are issued in a **family**;
   reuse of a rotated token triggers family revocation.

**Token lifetimes:** access **15 min** in production (200 min in the dev profile); refresh
**7 days** (`JwtOptions.AccessTokenExpirationMinutes` / `RefreshTokenExpirationDays`). The
`verify`/`refresh` responses report `expiresIn` = access-token seconds.

### Device binding & request signing

On first login the mobile app generates a hardware-backed keypair, keeps the private key in
the Secure Enclave / Keystore, and registers the public key (`/api/auth/device/*`). Protected,
money-moving endpoints (the checkout routes in `DeviceAuth:RequireSignatureForEndpoints`)
require a device **signature** verified by `DeviceSignatureMiddleware` (timestamp + nonce to
resist replay). Biometric/PIN unlock is a local gate on the device, not a server credential.

### Session revocation (immediate lockout)

JWTs are stateless, so identity is re-checked against the DB on **every** authenticated request:

- `users` carries `is_active` (default true) and a signed `token_version` claim.
- `SessionValidationMiddleware` (registered after authentication, before authorization) parses
  `sub` and returns **401** if the user row is missing/inactive, or if the request's
  `token_version` claim differs from the DB value.
- `token_version` is signed into the JWT, so it cannot be forged; bumping it invalidates every
  outstanding access **and** refresh token for that user.

Deactivating a user, bumping `token_version`, or deleting the user/device/refresh rows revokes
live sessions within one request (or on next app foreground — the mobile `useAuth` hook refetches
`/user/me` on resume and logs out on 401).

### Soft-delete & re-registration

Both the user and an admin can delete an account (`DELETE /api/users/me`,
`DELETE /api/admin/users/{id}`). Delete is **soft**: it sets `is_deleted=true`, `is_active=false`,
bumps `token_version`, and revokes active devices + refresh tokens — so the user is locked out
immediately via the machinery above. The `users.phone_number` unique index is **filtered on
`is_deleted = false`**, so a deleted phone can re-register (verifying a new code creates a fresh
row; old data stays hidden).

### Hardening already in place

- **JWT signing key** — a real `Jwt:Secret` (≥32 chars) is **required** in non-development
  environments; the app fails fast rather than falling back to a known key. The resolved secret
  is used for both signing and validation. Set `Jwt__Secret` in `deploy/.env` on the Droplet
  (read by `deploy/docker-compose.prod.yml`).
- **Hangfire dashboard** — gated by an `IDashboardAuthorizationFilter` that allows only
  authenticated `Admin` callers (or the dev bypass); previously it was open.
- **`verify-raw` diagnostic** — `POST /api/auth/device/verify-raw` returns `404` unless
  `Auth:DevBypass` is on, and the mobile app only calls it in `__DEV__` builds.
- **No response caching of admin data** — admin controllers do not use `[ResponseCache]`
  (which would turn authenticated responses into publicly cacheable ones).
- **Biometric keypair stability** — the app creates device keys only when absent and reuses the
  cached public key, instead of regenerating on every login.
- **Refresh-token & OTP storage** — both are hashed at rest with `SecretsHasher.Hash`
  (`SharedKernel/Security/SecretsHasher.cs`); refresh tokens are looked up by hash.
- **OTP CSPRNG** — codes come from `RandomNumberGenerator.GetInt32`, not `Random.Shared`.
- **Per-phone rate limiting** — `SendCodePolicy` and `VerifyCodePolicy` partition on the phone
  number, not just the IP (`Extensions/RateLimiterSetup.cs`). `X-Forwarded-For` is not trusted
  unconditionally: `KnownProxies` is cleared and trust is scoped to the container network or an
  explicit `ForwardedHeaders:TrustedNetworks` CIDR list.
- **HSTS / security headers** — set at the edge in the `security_headers` snippet in
  `deploy/Caddyfile`, imported by both sites; TLS terminates at Caddy on the Droplet.
- **Cross-user device rebinding refused** — registering a `device_id` already enrolled to a
  different account returns `409` instead of overwriting the victim's key/owner/status.
- **Logout ends the session** — revokes all of the user's refresh tokens and bumps
  `token_version`, so captured credentials do not survive logout.
- **Challenge issue/verify hardened** — challenges are keyed by their value (a known `device_id`
  cannot displace an outstanding challenge), stored only for enrolled active devices, and a
  non-base64 signature is an invalid signature rather than a 500.

### Real tables

`users` (+ `is_active`, `token_version`, `is_deleted`), `phone_verifications`, `devices`
(public keys), `refresh_tokens` (+ `family_id`).

---

## Why the dev bypass is a flag, not the environment

Previously the `000000` code and fake SMS were tied to `IsDevelopment()`, silently coupling the
bypass to `ASPNETCORE_ENVIRONMENT`. Because the then-current host (Render) ran the Development
profile, flipping it to Production would have made codes **random while SMS stayed fake** — a
total login lockout. The behavior is now driven by the explicit `Auth:DevBypass` flag: set it
`false` with real Twilio credentials to go live. (If Twilio is unconfigured and bypass is off,
the app still falls back to fake SMS with a warning, to avoid a hard failure mid-testing.)

---

## Secrets vs configuration

The August 2026 TestFlight incident — every install stuck on "INITIALIZING NETWORK..." against
a perfectly healthy backend — happened because these two categories were treated identically: a
hardening commit deleted the app's API base URL as if it were a leaked credential. It was
configuration, and removing it shipped apps that silently dialed `http://localhost:5000`.

The rule going forward:

| | Secrets | Configuration |
|---|---|---|
| Examples | Twilio/Monobank tokens, JWT secret, DB connection strings, keystores, `.p8` keys | API base URLs, feature flags, bundle IDs |
| Where they live | Environment variables / secret stores only (`deploy/.env`, EAS secret store) | In git, reviewable by anyone |
| If exposed | Rotate immediately | Nothing to rotate — it ships inside the binary anyway |

Enforced in code today:

- Mobile builds read `EXPO_PUBLIC_API_URL` from the `env` blocks in `mobile/eas.json` — one
  source of truth per build profile. `mobile/scripts/check-api-config.mjs` fails CI when a
  profile omits it, uses plain http, or when a second source (e.g. `app.json`
  `extra.apiUrl`) reappears.
- A non-dev build with no configured URL refuses to start (`resolveApiBaseUrl()` in
  `mobile/src/core/api/apiClient.ts`) instead of falling back to localhost. Misconfiguration
  must be loud at boot, not silent in the field.

---

## Future hardening (not yet implemented)

These are known gaps to close before a real-money launch:

- **Signature timestamp tolerance** — the device-signature window is 5 minutes
  (`DeviceAuthOptions.TimestampToleranceMs = 300000`, same value in
  `appsettings.Production.json:31`); tighten if feasible.
- **`Testing` config** — disables issuer/audience validation; must never be enabled in production.
- **App attestation** — Google Play Integrity / Apple App Attest to bind the app binary.
- **SSL pinning** — pin the API certificate/public key in the mobile client.
- **Signed OTA updates** — `mobile/app.json` enables an Expo update channel with no
  `expo.updates.codeSigningCertificate`, so whoever can publish to it can replace the app's
  JavaScript — including checkout. Tracked as **FF-03 (Critical)**; gated in CI by
  `mobile/scripts/check-update-signing.mjs`. Needs a keypair, a certificate baked into a new
  native build, and a store release.

For the full money-integrity review, see [Fraud analysis findings](#fraud-analysis-findings) below.

---

## Fraud analysis findings

Summary of the fraud/money-integrity review (2026-08; full analysis preserved in git
history, `docs/FRAUD_ANALYSIS.md` at commit history before removal). Severity = likelihood × impact.

| # | Question | Verdict | Severity |
|---|---|---|---|
| 1 | Can supplier upload fake QR? | ⚠️ Partially — QR authenticity is a heuristic (`QrMatrixVerifier`), not cryptographic | Medium |
| 2 | Can voucher already be redeemed? | ⚠️ Soft — redemption is **self-reported**, no POS verification | Medium |
| 3 | Can same voucher appear in two PDFs? | ✅ **No** — DB-global dedup by number OR QR payload | Low (protected) |
| 4 | Can insider steal inventory? | ⚠️ Not silently — admin voucher ops write audit events; nothing *alerts* on them yet | Low |
| 5 | Can admin manipulate margins? | ⚠️ Yes by design, but **audited**; margin only feeds the Profit report, checkout price is recomputed server-side | Low |

**Still soft by design rather than by defect:** redemption is self-reported and QR
authenticity is heuristic. Neither moves money on its own; both are accepted risks.

### Closed (enforced in code; details in the 2026-08-21 audit's refuted-hypotheses appendix)

- Unverified Monobank webhook — ECDSA (secp256k1) SHA-256 over the raw body (`X-Sign`),
  verified **fail-closed (401)**; amount mismatch → 400, no transition; replays with an
  older `ModifiedDate` are no-ops.
- Client-supplied checkout price — the price is recomputed server-side from `FuelPackages`.
- Unguarded callback duplication and race-unsafe redemption — the Assigned → Used
  transition is an atomic conditional `UPDATE ... WHERE Status == Assigned`, covered by
  integration tests against real Postgres.

### Open work packages

| Package | Item | Priority |
|---|---|---|
| WP-5 | Cryptographically signed voucher payloads (HMAC over provider/fuel/liters/expiry/number; validator first, supplier process change later) | Medium (long-term) |
| WP-6 | Detection & alerting: alert on webhook signature failures, amount mismatches, admin voucher deletions/bulk actions; treat a non-zero daily reconciliation difference as an incident, not a log line | Supporting |
| WP-7 | Backup & restore: tooling shipped (`deploy/backup.sh`, `restore.sh`); still owed — off-server copy (`BACKUP_REMOTE`) and one documented restore drill | Supporting (launch gate) |
| FF-05 | Written confirmation that the committed Monobank token was rotated **merchant-side** (owner attests rotation 2026-08-20; unverifiable without touching live systems), plus an answer on whether this repository was ever public | High |

### Monobank public key config

`Monobank:PublicKey` / `Monobank:Token` come from environment variables in `deploy/.env`
(never committed placeholders) — production fails fast if `Monobank:Enabled=true` with an
empty/placeholder key. To fetch the public key:

```
curl -H "X-Token: $MONOBANK_TOKEN" https://api.monobank.ua/api/merchant/pubkey
# -> {"key":"<base64-encoded PEM>"}
```

Set the returned `key` as `Monobank__PublicKey` (the app accepts base64-of-PEM verbatim).
If Monobank signs callbacks with an `X-Key-Id` header during key rotation, set
`Monobank__PublicKeys__<keyId>` for each active key — the controller resolves `X-Key-Id`
against that map and falls back to `PublicKey` when the header is absent. Never log raw
bodies or full `X-Sign` — log fingerprints only.
