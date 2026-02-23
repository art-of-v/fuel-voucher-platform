# FuelFlow — Architecture Review

**Reviewer:** Senior Solutions Architect (Adversarial Review)  
**Date:** 2026-02-23  
**Scope:** Full system — backend, mobile app, admin frontend, infrastructure, security, data model  
**Knowledge basis:** Complete codebase analysis (see README.md)

---

## 1. Executive Summary

FuelFlow is a voucher commerce system built with the bones of a reasonable MVP. The backend has been partially refactored into a Clean Architecture without completing the migration, resulting in **two routing systems running simultaneously in the same process** with overlapping endpoints, duplicated logic, and contradicting authentication checks. The mobile app works but is architecturally coupled to implementation details of the backend API in ways that will make evolution painful.

The security posture is **not acceptable for any production system handling financial transactions or user data.** There is no OTP randomness (all codes are `000000`), no admin authentication, phone numbers contain sensitive PII stored in plaintext, encrypted QR data has no key rotation strategy, and there are multiple unauthenticated or weakly-authenticated endpoints that return or modify user financial data.

At its current scale (3 users, 5 orders, 45 purchases from the DB dump), none of this causes operational problems. At 10,000 concurrent mobile users under a moderately skilled attacker, every critical issue listed below becomes a breach or service failure event.

### Top 5 Critical Risks

| Rank | Risk | Severity |
|---|---|---|
| 1 | **Universal OTP bypass** — `000000` authenticates any phone number, creating any user account | 🔴 Critical |
| 2 | **Zero admin authentication** — Admin frontend and all `/api/admin/*` routes are completely unauthenticated | 🔴 Critical |
| 3 | **Single-process stateful services** — FulfillmentConsumer, rate limiter, and ImportOrchestrator queue are all in-process memory; a crash or a second instance invalidates them | 🟠 High |
| 4 | **Dual routing conflict** — Both `routes.ts` and `router.ts` register overlapping endpoint paths; the one that wins depends on Express middleware registration order, creating silent inconsistencies | 🟠 High |
| 5 | **QR encryption key non-rotation** — All QR `qrCodeData` is encrypted with a single static key; there is no mechanism to rotate it without destroying all existing voucher data | 🟠 High |

---

## 2. Architecture Assessment

### What is Good

**Event-driven fulfillment core.** The decision to decouple order creation from voucher availability using an Outbox + Redis Streams pattern is the single best architectural decision in the system. The `SELECT ... FOR UPDATE SKIP LOCKED` in fulfillment assignment is race-condition safe and scales correctly under concurrent load. The fallback from Redis to DB polling is correctly implemented and genuinely resilient.

**Schema-first data layer.** Using Drizzle ORM with a single canonical `schema.ts` ensures the DB schema, TypeScript types, and Zod validators all derive from one source. This is the right approach and eliminates an entire class of bugs.

**Phone normalization (recent fix).** Normalizing Ukrainian phone formats (`067...`, `380...`, `+380...`) to a canonical E.164 form before DB lookup prevents duplicate user creation — a real production problem that was correctly identified and fixed.

**Explicit session persistence.** The `req.session.regenerate()` + `req.session.save()` sequence before responding to login ensures the session is durably stored before the app receives the token. This is correct and prevents a race condition where the client retries before the session exists.

**Structured logging.** Pino with child loggers and component labels is the right foundation. Every component has a named logger. This makes production debugging tractable.

---

### What is Problematic

**Partial migration frozen mid-state.** The codebase has two active HTTP routing systems. `routes.ts` registers routes for `/api/auth/phone/...`, `/api/vouchers/my`, `/api/purchases/...`. `router.ts` registers routes for the same paths via controllers. Express evaluates middleware in registration order. In `index.ts`, `registerRoutes(routes.ts)` is called when `USE_REFACTORED_ARCHITECTURE=false`, and `registerRefactoredRoutes(router.ts)` when `true`. This means in production (`USE_REFACTORED_ARCHITECTURE=true`), the legacy routes are not loaded — but the legacy code still exists, is imported, and could be accidentally activated. The bigger problem: there are endpoints defined only in `routes.ts` (e.g., `/api/purchases/session/:sessionId`, `/api/purchases/:id/complete`) that have no equivalent in `router.ts`, meaning they silently disappear in production mode.

**God-object `routes.ts`.** Even in legacy/fallback mode, `routes.ts` is 907 lines: session configuration, auth, purchases, QR codes, checkout, packages, stations, fuel types, users, referrals, admin, dev endpoints — all inline, all coupled. Authentication logic is reimplemented twice (`checkAuthorization` function in `routes.ts` vs `requireAuth` middleware in the new architecture). They behave differently: `checkAuthorization` falls back to a hardcoded dev UUID in non-production; `requireAuth` does not.

**`PurchaseService` creates N orders by iterating a loop.** When a user buys 3 vouchers, `simulatePayment()` calls `orderRepository.createWithEvent()` 3 times sequentially. Each call does a DB insert and a Redis publish. Under load, this is O(N) sequential writes where N is quantity—better to batch insert + single event.

**`ImportOrchestrator` uses in-memory job queue with no persistence.** A crash during PDF processing leaves `import_jobs` records stuck in `processing` status with no completion. The queue state is gone. Running a second backend instance creates two competing `ImportOrchestrator` singletons that will both try to process the same jobs.

**`purchases.sessionId` naming.** The column is named `sessionId` but stores the `userId`. This is a historical artifact from when session IDs were used as user identifiers. It creates confusion when reading queries like `WHERE sessionId = userId` and would cause bugs for anyone not familiar with this history.

---

### What is Dangerous

**OTP is `000000` always.** `AuthService.generateVerificationCode()` returns the hardcoded string `"000000"`. The Twilio SMS is sent (or logged in dev mode), but the code checked is always `000000`. Any attacker who knows any user's phone number can authenticate as them in two API calls without any SMS access. This is not a bug — it was intentionally coded. It is deployed to production at `https://fuel-flow-admin-panel-bac.onrender.com`.

**All `/api/admin/*` endpoints are unauthenticated.** There is no middleware, no token, no session check on any admin route. The admin frontend is served on port 5002. Anyone who knows the backend URL can call `DELETE /api/admin/vouchers` and destroy the entire voucher inventory, or call `POST /api/admin/stations` to inject arbitrary data. The backend URL is easily discoverable from the mobile app's network traffic.

**`checkAuthorization` in `routes.ts` falls back to a hardcoded UUID.** In non-production mode (which is the current Render deployment — `NODE_ENV=development` is set in `render.yaml`), any request to a guarded route without a session is automatically authenticated as user `d366f82a-e65c-4110-bf20-ab2f44750cfe`. Since Render is running with `NODE_ENV=development`, this UUID becomes a superuser with access to all purchases, vouchers, and checkout endpoints — accessible to any client.

**`render.yaml` sets `NODE_ENV=development` on the production server.** This is the root cause of the dev-fallback being active in production. It also disables `secure` cookies in some code paths, enables the `/api/auth/dev-login` endpoint, and triggers verbose logging.

---

## 3. Critical Issues (MUST FIX)

### Issue 1: Universal OTP Bypass

**Description:** `AuthService.generateVerificationCode()` always returns `"000000"`. Any person who knows a target user's phone number can authenticate as them using this code.

**Why it is a problem:** Authentication is the security boundary for all user data access. Bypassing it means any attacker can access any user's vouchers, purchase history, and place fraudulent orders billed to any account.

**Real-world failure scenario:** An attacker enumerates phone numbers from any public directory, sends `POST /api/auth/phone/send-code` for each, then sends `POST /api/auth/phone/verify` with code `000000` for each. They are now authenticated as every user. Against a fuel voucher system, this means stealing voucher codes that represent real monetary value.

**Fix:**
```typescript
// auth.service.ts
generateVerificationCode(): string {
    // Cryptographically random 6-digit code
    const array = new Uint32Array(1);
    crypto.getRandomValues(array);
    return String(array[0] % 1000000).padStart(6, '0');
    // Node.js equivalent:
    // return String(Math.floor(crypto.randomInt(0, 1000000))).padStart(6, '0');
}
```
This is a one-line fix with zero compatibility impact. Do this before any other change.

---

### Issue 2: No Admin Authentication

**Description:** All `/api/admin/*` routes — station management, package management, voucher deletion, purchase viewing, user listing — have zero authentication or authorization checks.

**Why it is a problem:** The backend URL is observable from mobile app traffic. Any client can call admin endpoints directly. The admin frontend itself has no login screen.

**Real-world failure scenario:** A user discovers the API URL from their phone's network traffic (trivial with Charles Proxy or similar). They call `POST /api/admin/vouchers/bulk-action` with `{ "action": "delete_all" }`. All voucher inventory is destroyed. This is a single HTTP request.

**Fix:** Minimum viable fix is a static admin API key checked as a middleware:
```typescript
// middleware/admin-auth.middleware.ts
export function requireAdminKey(req: Request, res: Response, next: NextFunction): void {
    const key = req.headers['x-admin-key'];
    if (!key || key !== process.env.ADMIN_API_KEY) {
        throw AppError.unauthorized('Admin access required');
    }
    next();
}
```
Apply to all `/api/admin/*` routes in `router.ts`. The admin frontend sends this header. Add `ADMIN_API_KEY` to environment variables. Long-term, implement proper admin user accounts with session-based auth.

---

### Issue 3: `NODE_ENV=development` in Production

**Description:** `render.yaml` sets `NODE_ENV=development`. This activates: (a) the dev authentication fallback in `routes.ts` that authenticates any sessionless request as a hardcoded user, (b) the `/api/auth/dev-login` endpoint, (c) lax cookie settings in some code paths, and (d) verbose logging that may expose internal details.

**Why it is a problem:** The production server is operating as if it is a local development environment. Every dev bypass and debug feature is enabled for public traffic.

**Real-world failure scenario:** The `checkAuthorization` middleware in `routes.ts` — which is still partially active via route prefixes like `/api/vouchers/my` — grants full access to user ID `d366f82a-e65c-4110-bf20-ab2f44750cfe` for any unauthenticated request. Any attacker who calls `/api/purchases/my` without a session cookie gets this user's purchase history.

**Fix:**
```yaml
# render.yaml
- key: NODE_ENV
  value: production  # NOT development
```
Then audit every `process.env.NODE_ENV !== 'production'` check in the codebase to verify the behavior change is intentional.

---

### Issue 4: Dual Routing System with Silent Endpoint Loss

**Description:** `routes.ts` and `router.ts` are both partially active depending on the `USE_REFACTORED_ARCHITECTURE` flag. Several endpoints exist only in one or the other, with no tests or documentation of which ones are active in production.

**Endpoints in `routes.ts` with no equivalent in `router.ts`:**
- `GET /api/purchases/session/:sessionId` (returns purchases by raw user ID — unauthenticated!)
- `POST /api/purchases/:id/complete` (legacy direct fulfillment endpoint)
- `GET /api/vouchers/my` (duplicates `router.ts` but with different auth logic)

**Why it is a problem:** Silent scope reduction when switching the flag, inconsistent authentication behavior between equivalent endpoints, and the unauthenticated `GET /api/purchases/session/:sessionId` endpoint exposes any user's full purchase history to anyone who knows their UUID.

**Fix:** Do one of the following:
1. **Complete the migration:** Port every remaining endpoint from `routes.ts` to `router.ts` controllers, delete `routes.ts`, remove the feature flag.
2. **Freeze the legacy path:** If flag will stay, add explicit tests that assert the same endpoints exist in both routing modes and return the same authorization behavior.

Priority: Option 1. The migration is 80% done. Completing it eliminates the dual-system cognitive overhead permanently.

---

### Issue 5: QR Code Encryption Without Key Management

**Description:** All voucher `qrCodeData` is AES-256 encrypted with a single static key stored in `QR_ENCRYPTION_KEY` env var. There is no key versioning, rotation strategy, or per-record key metadata.

**Why it is a problem:** If the key is compromised, all historical voucher data is immediately decryptable. If the key needs to be rotated, every voucher record must be re-encrypted in a single, dangerous migration — with no rollback.

**Real-world failure scenario:** A key appears in a git history commit (`.env` accidentally committed), a Render environment variable is leaked via a log statement, or an employee export. The attacker can decrypt QR codes for all vouchers in the database, enabling them to present vouchers at fuel stations.

**Fix:**
1. Add a `keyVersion` column to the `vouchers` table.
2. When encrypting, store the key version alongside the ciphertext.
3. Implement a rotation sequence: generate new key, decrypt with old key + re-encrypt with new key in batches, update `keyVersion`, retire old key.
4. For new vouchers, always use the current key version.

---

## 4. Scalability Improvements

### Current Limitations

| Component | Limit | Root Cause |
|---|---|---|
| FulfillmentConsumer | 1 instance | In-process singleton; second instance creates duplicate processing |
| ImportOrchestrator | 1 instance | In-memory job queue; not shareable across processes |
| Rate limiter | 1 instance | In-memory `Map`; resets on restart, not shared across instances |
| Session store | Scales fine | PostgreSQL-backed; already externalized |
| DB connection pool | ~10 connections (PG default) | Not configured explicitly; under load, requests queue |

### Redesign Suggestions

**FulfillmentConsumer — make it horizontally safe:**
The consumer already uses Redis consumer groups which *are* designed for multiple consumers. The singleton pattern in code (`FulfillmentConsumer.start()`) prevents a second instance from competing. Remove the singleton guard. Each backend instance registers as a unique consumer in the group (use hostname/instance ID as consumer name). Redis consumer groups handle deduplication. Zero code change in the fulfillment logic — only the registration pattern changes.

**Rate limiter — externalize to Redis:**
Replace the in-memory `Map` with Redis `INCR` + `EXPIRE`:
```typescript
async function isRateLimited(key: string, limit: number, windowSec: number): Promise<boolean> {
    const count = await redis.incr(key);
    if (count === 1) await redis.expire(key, windowSec);
    return count > limit;
}
```
This is stateless, survives restarts, and works across instances.

**ImportOrchestrator — use BullMQ:**
Replace the in-memory queue with [BullMQ](https://bullmq.io/) backed by Redis. Jobs survive restarts, support retries with backoff, and can run in a separate worker process if the OCR pipeline becomes CPU-intensive enough to block the Express event loop (Gemini API calls are I/O-bound and fine, but `pdfjs-dist` rendering is CPU-bound).

**DB connection pooling — configure explicitly:**
```typescript
// db.ts
const pool = new Pool({
    connectionString: process.env.DATABASE_URL,
    max: 20,               // Explicit max connections
    idleTimeoutMillis: 30000,
    connectionTimeoutMillis: 2000,
});
```
With Supabase's session-mode pooler on port 6543, individual connections are fine. With transaction-mode pooler (port 5432), set `max` lower and avoid long transactions.

**Database read replicas for reporting:**
Admin panel queries (`GET /api/admin/vouchers` with pagination, `GET /api/admin/purchases`) are read-heavy analytics queries. Route these to a read replica. This is zero-effort if using Supabase (enable read replicas in dashboard, use a separate `DATABASE_URL_REPLICA` env var).

---

## 5. Performance Optimizations

### Hot Path: User Voucher Retrieval (`GET /api/vouchers/my`)

The `getUserVouchers()` function in `vouchers.repository.ts` runs a complex query with joins and fuzzy matching aliases (`getFuelAliases()` returns array of possible string matches per fuel type). Under load from polling mobile apps, this is executed frequently with non-trivial CPU cost.

**Fix:** Cache per-user voucher lists in Redis with a short TTL (30–60 seconds). Invalidate on fulfillment or when voucher status changes. This eliminates the majority of DB load for the most frequently called user-facing endpoint.

```typescript
const cacheKey = `vouchers:user:${userId}`;
const cached = await redis.get(cacheKey);
if (cached) return JSON.parse(cached);
const vouchers = await db.query(...);
await redis.setEx(cacheKey, 60, JSON.stringify(vouchers));
return vouchers;
```

### Missing Database Indexes

The following query patterns exist in code with no corresponding indexes in the schema:

| Query | Missing Index |
|---|---|
| `WHERE phone = ?` on `phone_verifications` | `CREATE INDEX ON phone_verifications (phone, created_at DESC)` |
| `WHERE assignedToUserId = ?` on `vouchers` | `CREATE INDEX ON vouchers (assigned_to_user_id)` |
| `WHERE status = 'available' AND provider = ? AND fuelType = ?` on `vouchers` | `CREATE INDEX ON vouchers (status, provider, fuel_type)` |
| `WHERE userId = ? AND status = ?` on `orders` | `CREATE INDEX ON orders (user_id, status)` |
| `WHERE sessionId = ?` on `purchases` | `CREATE INDEX ON purchases (session_id)` |

The `vouchers` table is the hottest table in the system (every fulfillment reads it with `SELECT ... FOR UPDATE SKIP LOCKED`). Without a composite index on `(status, provider, fuel_type)`, this query does a full table scan, which becomes catastrophic at 100k+ rows.

**Add these immediately via Drizzle schema.**

### Sequential Order Creation in `simulatePayment`

```typescript
// Current: sequential, N DB writes
for (let i = 0; i < purchase.quantity; i++) {
    await this.orderRepository.createWithEvent(...);
}
```

Replace with a batch insert:
```typescript
// Better: single DB roundtrip
const orderData = Array.from({ length: purchase.quantity }, (_, i) => ({...}));
await this.orderRepository.batchCreateWithSingleEvent(orderData);
```

### ImportOrchestrator: CPU-blocking PDF Rendering

`pdfjs-dist` renders PDF pages synchronously on the main thread. For a 50-page import PDF, this blocks the Node.js event loop for potentially seconds, making the server unresponsive to all other requests during that window.

**Fix:** Move PDF rendering to `worker_threads`. Wrap `convertPdfToImages()` in a `Worker` thread pool. The Gemini API calls are already async and non-blocking.

---

## 6. Security Review

### 6.1 Authentication

**OTP is `000000` always (Critical — covered in §3.1)**

**Phone verification has no expiry enforcement in the query:**
`verificationRepository.getLatestPhoneVerification(phone)` returns the most recent non-expired record, but the expiry check is SQL-side. Verify that `WHERE expires_at > NOW()` is in the query. If not, expired codes work forever.

**Recommendation:** Add explicit `verified = FALSE AND expires_at > NOW()` to the verification lookup query.

**Session fixation risk:**
`req.session.regenerate()` is correctly called on login, which mitigates session fixation. However, `devLogin` in `auth.controller.ts` calls `regenerate()` but does not set `session.phoneAuth = true`. This means the dev session will fail the `session.phoneAuth` check in `getCurrentUser()` and return 401. The `devLogin` endpoint creates a broken session.

**Fix:**
```typescript
(req.session as any).userId = user.id;
(req.session as any).phoneAuth = true;  // ADD THIS
```

### 6.2 Authorization

**`GET /api/purchases/session/:sessionId` — completely unauthenticated user data leak:**
This endpoint accepts a `sessionId` URL parameter and returns all purchases for that user ID. There is no authentication check. Any client can fetch any user's purchase history by guessing or brute-forcing a UUID.

```bash
curl https://fuel-flow-admin-panel-bac.onrender.com/api/purchases/session/760c6864-6f1c-47af-9943-c38958607909
# Returns all purchases for user ID in plain text
```

**Fix:** Add `checkAuthorization` middleware and verify `sessionId === req.authUserId`.

**All `/api/admin/*` routes — unauthenticated (Critical — covered in §3.2)**

**Voucher ownership not verified before mark-as-used:**
`VoucherController.markUsed()` comment: *"Note: We should verify ownership, but for parity with legacy logic let's just proceed."* Any authenticated user can call `PATCH /api/vouchers/:id/mark-used` with any voucher ID and mark it used — including vouchers belonging to other users.

**Fix:**
```typescript
const voucher = await this.voucherService.getVoucherById(id);
if (!voucher || voucher.assignedToUserId !== userId) {
    res.status(403).json({ error: 'Forbidden' });
    return;
}
```

### 6.3 Input Validation

**Phone number validation removed in recent refactor:**
In the legacy `routes.ts`, the phone number was validated against a regex before proceeding. In the recent normalization refactor, this validation was replaced with silent normalization. An invalid phone string like `abc` will be passed to Twilio, causing a downstream error rather than a clean 400 response.

**Fix:** Validate E.164 format *after* normalization:
```typescript
const normalized = this.normalizePhone(phone);
if (!/^\+[1-9]\d{6,14}$/.test(normalized)) {
    throw AppError.badRequest('Invalid phone number');
}
```

**Voucher bulk-action accepts arbitrary `targetUserId`:**
`POST /api/admin/vouchers/bulk-action` with `action: "assign"` and `targetUserId` assigns vouchers to any user ID, including non-existent UUIDs. The `targetUserId` is written to the `vouchers` table without verifying the user exists.

**Fix:** Validate `targetUserId` exists in `users` table before assignment.

**`insertPurchaseSchema` does not validate numeric bounds:**
`liters`, `price`, `quantity` are validated as integers but with no upper bounds. A malicious client could create a purchase for `999999999` liters at `0` price.

**Fix:** Add Zod `min`/`max` constraints to numeric fields in `schema.ts`.

### 6.4 Data Protection

**QR `qrCodeData` is encrypted, but stored `qrCodeUrl` is plaintext:**
Some vouchers also store a `qrCodeUrl` field. This URL is the direct link to the QR code image hosted on an external service (`api.qrserver.com`). The URL contains the voucher's raw data as a query parameter: `?data=VOUCHER:abc123`. The encryption of `qrCodeData` is bypassed by the plaintext URL.

**Fix:** Purge `qrCodeUrl` fields from vouchers after migration to the encrypted model. Do not store plaintext QR data in any form.

**PII in logs:**
The backend logs requests including the URL path. Phone numbers appear in paths like `POST /api/auth/phone/verify` (body, not path — fine). But the `[DEV] Verification code for +380677... : 000000` log line in `routes.ts` writes a user's phone number to the log stream. On Render, logs are retained and searchable.

**Fix:** Remove phone number from log output. Log only a hash or the last 4 digits:
```typescript
const maskedPhone = phone.slice(0, 4) + '****' + phone.slice(-2);
log.info({ phone: maskedPhone }, 'Verification code sent');
```

**Sessions not cleared server-side on token expiry:**
The `connect-pg-simple` store expires sessions based on `expire` column. However, old sessions accumulate in the `sessions` table unless `connect-pg-simple`'s pruning is enabled.

**Fix:** Enable session pruning:
```typescript
new PostgresStore({
    pool,
    tableName: 'sessions',
    pruneSessionInterval: 60 * 60, // prune every hour
})
```

### 6.5 Transport Security

**CORS allows any origin:**
```typescript
const origin = req.headers.origin;
if (origin) {
    res.header('Access-Control-Allow-Origin', origin);
}
```
This is a wildcard CORS policy that reflects any origin. Combined with `Access-Control-Allow-Credentials: true`, this is a misconfiguration. A page on any domain can make credentialed requests to the API and receive session cookies.

**Fix:** Allowlist specific origins:
```typescript
const ALLOWED_ORIGINS = [
    'https://yourapp.com',
    'capacitor://localhost',  // iOS Capacitor
    'http://localhost:5173',  // local admin dev
];
if (ALLOWED_ORIGINS.includes(origin)) {
    res.header('Access-Control-Allow-Origin', origin);
    res.header('Vary', 'Origin');
}
```

**No HTTPS enforcement at application level:**
The backend relies on Render's TLS termination. This is fine for production but means local dev over HTTP does not work with `SameSite=None; Secure` cookies, breaking physical device testing. This is already documented as a known issue but should be noted here as an architectural tension.

### 6.6 API Abuse

**No global request size limit:**
Express is configured with `express.json()` but without an explicit body size limit. The default is 100kb. Gemini-returning responses (from OCR) can be large, but request bodies from mobile clients should be bounded. A malicious client can send large JSON payloads to any endpoint.

**Fix:**
```typescript
app.use(express.json({ limit: '10kb' }));
// For import endpoint specifically:
multer({ limits: { fileSize: 50 * 1024 * 1024 } })  // already done, keep this
```

**OTP brute-force via IP rotation:**
The rate limiter keys on IP address. An attacker with access to even 5 different IP addresses (trivially available via proxies) can send 15 OTP verify attempts per 5-minute window. Against a 6-digit code, this is `15 / 1,000,000 = 0.0015%` per window. Over 2 hours: `(120 min / 5 min) * 15 = 360 attempts = 0.036%` success probability. With `000000` always being the code, this is irrelevant — it's always try one code and succeed. After fixing OTP, add per-phone rate limiting in addition to per-IP.

---

## 7. Maintainability Improvements

### Dual Routing System Must Be Resolved

The coexistence of `routes.ts` and `router.ts` is the single biggest maintainability hazard. Every developer must understand which routes are "active" in which mode, which auth middleware applies to which endpoint, and which version of a behavior is canonical. This cognitive overhead is not acceptable.

**Action:** Sprint to complete the migration. The remaining work in `routes.ts` that has no equivalent in `router.ts`:
1. `GET /api/purchases/session/:sessionId` — port or delete (it's a security hole anyway)
2. `POST /api/purchases/:id/complete` — port to `PurchaseController`
3. Admin endpoints for purchases, Stripe config, referrals — consolidate into `router.ts`

Once done, delete `routes.ts`, remove the `USE_REFACTORED_ARCHITECTURE` flag, and simplify `index.ts` to a single code path.

### `purchases.sessionId` Column Rename

The column storing user ID in the `purchases` table is named `sessionId`. This is misleading and causes cognitive dissonance when reading queries. Rename to `userId` with a migration:
```sql
ALTER TABLE purchases RENAME COLUMN session_id TO user_id;
```
Update all references in `purchases.repository.ts`, `routes.ts`, `purchase.service.ts`.

### Error Handling Inconsistency

The backend has three different error response shapes depending on which code path handles the error:

1. New controllers + `errorHandler` middleware → `{ code, message, statusCode, correlationId }`
2. Legacy `routes.ts` → `{ error: "message" }` or `{ message: "message" }`
3. `AppError` thrown without handler → varies

Mobile app code pattern-matches on `error.message` or response body structure, which means changing the error format silently breaks the app's error display.

**Fix:** Define a single error response schema and enforce it across all paths. The `errorHandler` middleware in the Clean Architecture side does this correctly. Completing the migration to `router.ts` resolves this automatically.

### Type Safety Gaps

`any` appears in:
- `req as any` in multiple controllers (should be `AuthenticatedRequest`)
- `voucherService.updateVoucher(id, { status: 'used' as any })` — the cast hides a typing problem where the status type union doesn't include `'used'`
- `purchaseRepository.getPurchaseWithQrCode` return type is `any`

These are entry points for bugs at refactor time. Replace with proper types.

### Test Coverage Is Effectively Zero

The `tests/` directory exists and has Vitest configured but all tests appear to be stubs or integration skeletons. There are no unit tests for:
- `AuthService.normalizePhone()` — recently modified, untested
- `AuthService.verifyPhone()` — the critical authentication path
- `FulfillmentConsumer` voucher assignment logic
- `PurchaseService.simulatePayment()` order splitting

**Minimum required:** Unit tests for the authentication and fulfillment paths with mock repositories. These are the highest-value, lowest-effort tests to write.

---

## 8. Quick Wins

These can be done in under a day each:

| Fix | Impact | Effort |
|---|---|---|
| Replace `000000` with `crypto.randomInt()` | 🔴 Removes critical auth bypass | 10 minutes |
| Set `NODE_ENV=production` in `render.yaml` | 🔴 Disables all dev fallbacks in production | 5 minutes |
| Add admin API key middleware to `/api/admin/*` | 🔴 Eliminates unauthenticated admin access | 2 hours |
| Remove `GET /api/purchases/session/:sessionId` | 🔴 Removes unauthenticated data leak endpoint | 15 minutes |
| Add DB indexes on `vouchers`, `orders`, `purchases` | 🟠 10–100x query speedup at scale | 1 hour (schema changes) |
| Replace reflected CORS with allowlist | 🟠 Eliminates CSRF via cross-origin credentialed requests | 30 minutes |
| Add `pruneSessionInterval` to `connect-pg-simple` | 🟡 Prevents `sessions` table from growing unboundedly | 5 minutes |
| Mask phone numbers in log output | 🟡 Removes PII from log streams | 30 minutes |
| Fix `devLogin` to set `session.phoneAuth = true` | 🟡 Fixes broken dev login session | 5 minutes |
| Add `express.json({ limit: '10kb' })` request size cap | 🟡 Prevents body-size DoS | 5 minutes |
| Add ownership check in `PATCH /api/vouchers/:id/mark-used` | 🟠 Prevents cross-user voucher manipulation | 30 minutes |

---

## 9. Long-Term Evolution

### Target Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Mobile App (React Native / Expo)                           │
│  Push notifications via Expo / FCM / APNs                   │
└────────────────────────┬────────────────────────────────────┘
                         │ HTTPS + JWT (stateless)
┌────────────────────────▼────────────────────────────────────┐
│  API Gateway / Edge (Cloudflare / Nginx)                    │
│  TLS termination · Rate limiting · WAF                      │
└──────┬───────────────────────────────────────┬──────────────┘
       │                                       │
┌──────▼──────────┐                   ┌────────▼──────────────┐
│  Public API     │                   │  Admin API            │
│  (Stateless)    │                   │  (Separate service    │
│  Horizontally   │                   │   or subdomain)       │
│  scalable       │                   │  Role-based auth      │
└──────┬──────────┘                   └────────┬──────────────┘
       │                                       │
┌──────▼───────────────────────────────────────▼──────────────┐
│  Shared PostgreSQL (read/write split)                       │
│  Supabase or RDS with connection pooling (PgBouncer)        │
└─────────────────────────────────────────────────────────────┘
       │
┌──────▼──────────────────────────────────────────────────────┐
│  BullMQ Job Queue (Redis-backed)                            │
│  ├── voucher-fulfillment (worker pool, scalable)            │
│  └── pdf-import (worker pool, CPU-isolated)                 │
└─────────────────────────────────────────────────────────────┘
```

Key changes from current state:
1. **JWT instead of sessions** — eliminates session store as a stateful shared dependency, enables true horizontal scaling
2. **Separate admin service** — admin and public APIs are separate deployments with separate auth, preventing shared attack surface
3. **BullMQ** — replaces in-memory queues with persistent, retry-capable, distributed job system
4. **Push notifications** — replace mobile polling (`/api/sync`) with server-push for fulfillment events

### Migration Path

**Phase 1 — Security (Week 1):** Fix all critical security issues (§3). Zero architectural change, purely correctness fixes. This is non-negotiable and should be done immediately.

**Phase 2 — Complete Migration (Week 2–3):** Port all remaining `routes.ts` endpoints to `router.ts` controllers. Delete `routes.ts`. Remove feature flag. Rename `sessionId` → `userId` in `purchases`.

**Phase 3 — Add Indexes + Redis Rate Limit (Week 3):** Add all missing DB indexes (10 minutes of SQL, huge query improvement). Move rate limiter to Redis INCR/EXPIRE.

**Phase 4 — BullMQ (Week 4–5):** Replace `ImportOrchestrator` in-memory queue with BullMQ. Add retry logic with exponential backoff. Move fulfillment to a separate BullMQ worker. This makes both the import pipeline and fulfillment horizontally scalable.

**Phase 5 — JWT + Stateless API (Month 2):** Replace session cookies with short-lived JWTs (15 minutes) + refresh tokens stored in PostgreSQL. Eliminates the `sessions` table as a shared stateful dependency. Mobile app passes `Authorization: Bearer <token>` header — resolves cookie cross-device issues permanently. This is the biggest architectural change and should be done carefully with a migration period supporting both auth mechanisms.

**Phase 6 — Admin Separation + Push (Month 3):** Split admin routes into a separate Render service with its own domain and dedicated admin user accounts. Implement Expo Push Notifications to replace polling on the mobile app for fulfillment events.

---

*This review reflects the system state as of 2026-02-23. Issues are ranked by real-world exploitability and operational impact, not theoretical elegance. The most important actions are in §8 Quick Wins — they are fast, cheap, and address critical security gaps.*
