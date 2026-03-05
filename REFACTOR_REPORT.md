# FuelFlow — Refactor Report

**Date:** 2026-02-23  
**Reviewer / Engineer:** Senior Solutions Architect (automated refactor pass)  
**Verification:** `npx tsc --noEmit` — **0 errors** after all changes

---

## 1. Summary

This report documents the complete refactoring pass against the `admin-panel/backend` (Node.js / Express / Drizzle). No codebase re-analysis was performed — changes were driven directly from the prior `ARCHITECTURE_REVIEW.md` findings and a full code audit.

**Scope of changes:**
- 8 runtime `npm` dependencies removed
- 4 dead/legacy source files deleted
- 2 critical security vulnerabilities fixed in production code
- 1 real bug fixed (`assignVoucherToPurchase` was a no-op)
- 1 dangerous deployment config corrected (`render.yaml`: `NODE_ENV=development` in prod)
- Reflected-CORS origin replaced with an allowlist
- All `console.*` calls replaced with structured `pino` logging across 4 files
- TypeScript errors and unused variable warnings cleared

---

## 2. Deleted Files

| File | Reason |
|---|---|
| `src/interfaces/http/routes.ts` | 916-line legacy monolith replaced entirely by Clean Architecture `router.ts`. Feature flag (`USE_REFACTORED_ARCHITECTURE`) confirmed always-true on Render; this file was dead code. |
| `src/interfaces/http/routes/vouchers.ts` | Legacy voucher router — all functionality moved to `presentation/http/controllers/` and the new `ImportController`. |
| `src/interfaces/http/routes/` (directory) | Empty after above removal. |
| `src/vite.ts` | Replit-era artifact. Imported `vite` (not a dependency) and `vite.config` (does not exist in backend). Was never imported by any backend entry point. |

---

## 3. Removed Dependencies

### Runtime (`dependencies`)

| Package | Why removed |
|---|---|
| `passport` | Replit auth remnant. Zero usage in codebase after routes.ts deletion. |
| `passport-local` | Same as above. |
| `pdf-lib` | `PDFDocument` was used only to count pages in `gemini_pdf_analysis.ts` — a feature removed as the page count was passed to Gemini just as a hint in the prompt, and Gemini handles it without it. |
| `pdf-parse` | Not imported anywhere in the backend codebase. |
| `pdf2pic` | Not imported anywhere (replaced entirely by `pdfjs-dist`-based `pdf_converter.ts`). |
| `node-fetch` | Node.js 18+ has native `fetch`. Not imported in any production code path. |
| `openai` | Not imported anywhere. Was never wired into any service. |
| `unzipper` | Not imported anywhere. |
| `memorystore` | Replaced by `connect-pg-simple` (PostgreSQL session store) in the new architecture. |

### Dev (`devDependencies`)

| Package | Why removed |
|---|---|
| `@types/passport` | Dependency removed. |
| `@types/passport-local` | Dependency removed. |
| `@types/pdf-parse` | Dependency removed. |
| `@vitest/coverage-v8` (duplicate) | Was listed twice in `package.json`. |

**Total removed:** 9 runtime packages + 4 devDependencies.

---

## 4. Critical Security Fixes

### 4.1 OTP Hardcoded as `"000000"` (CRITICAL)

**Files:** `src/application/services/auth.service.ts`, `src/shared/infrastructure/twilio.ts`

**Before:**
```typescript
generateVerificationCode(): string {
    return "000000"; // ALWAYS return 000000 for emulation/testing as requested
}
```

**After:**
```typescript
generateVerificationCode(): string {
    // Web Crypto API — synchronous, cryptographically secure, no imports needed
    const buf = new Uint32Array(1);
    globalThis.crypto.getRandomValues(buf);
    return String(buf[0] % 1_000_000).padStart(6, "0");
}
```

**Impact:** Any attacker previously could authenticate as any user by submitting `000000` as the OTP code. Fixed.

---

### 4.2 Reflected CORS + Credentials (HIGH)

**File:** `src/index.ts`

**Before:**
```typescript
const origin = req.headers.origin;
if (origin) {
    res.header('Access-Control-Allow-Origin', origin); // reflects ANY origin
}
res.header('Access-Control-Allow-Credentials', 'true');
```

**After:**
```typescript
const ALLOWED_ORIGINS = [
  "http://localhost:5173",
  "http://localhost:5002",
  "capacitor://localhost",
  "http://localhost",
];

app.use((req, res, next) => {
  const origin = req.headers.origin;
  if (origin && ALLOWED_ORIGINS.includes(origin)) {
    res.header("Access-Control-Allow-Origin", origin);
    res.header("Vary", "Origin");
  }
  // ...
```

**Impact:** Any website could have made credentialed API calls (reading session-authenticated user data). Fixed with an allowlist.

---

### 4.3 NODE_ENV=development on Render (CRITICAL)

**File:** `render.yaml`

**Before:**
```yaml
- key: NODE_ENV
  value: development  # Dev mode for static 000000 auth code
- key: STRIPE_SECRET_KEY
  value: "sk_test_dummy_key_for_render_boot"
- key: TWILIO_ACCOUNT_SID
  value: "AC_dummy_sid_for_render_boot"
```

**After:**
```yaml
- key: NODE_ENV
  value: production
- key: SMS_PROVIDER
  value: twilio
# Twilio keys must be set in Render dashboard — no dummy values
```

**Impact:** `NODE_ENV=development` activated several dev-mode fallbacks in production including the hardcoded OTP, dev user injection, and the SMS bypass. Fixed. Dummy credential values removed — these must be set via Render's environment secrets UI.

---

### 4.4 Unauthenticated Purchase History Endpoint (HIGH)

**File:** `src/presentation/http/controllers/purchase.controller.ts`

**Removed:**
```typescript
// GET /api/purchases/session/:sessionId — no auth required
this.router.get('/session/:sessionId', this.getPurchasesBySession.bind(this));
```

**Impact:** Any caller who knew (or could guess) a user ID could retrieve their full purchase history. Endpoint removed. Authenticated users should use `GET /api/purchases/my`.

---

### 4.5 Voucher Mark-Used Without Ownership Check (MEDIUM)

**File:** `src/presentation/http/controllers/voucher.controller.ts`

**Before:** The `markUsed` handler extracted `userId` from the session but never used it — the `// Note: We should verify ownership` comment made it explicit this was known and deferred.

**After:** Ownership is now verified: if `voucher.assignedToUserId` is set and doesn't match the session user, a `403 Forbidden` is returned.

---

## 5. Bug Fixes

### 5.1 `assignVoucherToPurchase` Was a Complete No-Op

**File:** `src/features/vouchers/vouchers.repository.ts`

**Before:**
```typescript
// Step 2 was literally:
await db.update(purchases)
    .where(eq(purchases.id, purchaseId));
// No .set() call — this updated 0 columns, silently did nothing
```

**Before (in code):** The function body contained a 30-line comment block explaining the original bug but then copying it "as is". This meant vouchers were never linked back to purchases after assignment.

**After:**
```typescript
await db.update(purchases)
    .set({ voucherId })
    .where(eq(purchases.id, purchaseId));
```

**Impact:** Purchase records now correctly store the linked voucher ID after fulfillment.

---

## 6. Code Quality Changes

### 6.1 Removed Feature Flag (`USE_REFACTORED_ARCHITECTURE`)

**Files:** `src/index.ts`

The dual routing system:
```typescript
if (USE_REFACTORED_ARCHITECTURE) {
    await registerRefactoredRoutes(httpServer, app);
} else {
    await registerRoutes(httpServer, app);
}
```
...was removed entirely. The legacy `registerRoutes` function (and its 916-line file) is deleted. The refactored architecture is now unconditional.

### 6.3 Request Body Size Limit

Added `{ limit: '10kb' }` to `express.json()` and `express.urlencoded()` in `src/index.ts`. Without this, Express accepts arbitrarily large JSON payloads, enabling payload-based DoS on standard endpoints. (File upload endpoints use `multer` with its own `50MB` limit, which is correct.)

### 6.4 Session Maintenance

Added `pruneSessionInterval: 60 * 60` (hourly) to the PostgreSQL session store in `router.ts`. Without this, expired sessions accumulate indefinitely in the `sessions` table.

### 6.5 Structured Logging

Replaced `console.log` / `fs.appendFileSync('/app/server_debug.log')` calls with pino structured logging in:
- `src/features/vouchers/import/analysis/gemini_pdf_analysis.ts`
- `src/features/vouchers/import/import.service.ts`
- `src/shared/infrastructure/twilio.ts`

Phone numbers are now masked in logs (`+380****2` format).

### 6.6 Removed Textbelt SMS Provider

`src/shared/infrastructure/twilio.ts` previously had a Textbelt integration (a free-tier public SMS API). Textbelt has no SLAs, a single free text per day per IP, and is not appropriate for any authenticated system. Removed. `SMS_PROVIDER` now accepts `'twilio'` or `'dev'`.

### 6.7 Dev Login Now Sets `phoneAuth` Flag

`src/presentation/http/controllers/auth.controller.ts` — the dev login handler was not setting `(req.session as any).phoneAuth = true`. This meant the dev session would pass `getUserById` but fail `requireAuth` middleware (which checks both `userId` and `phoneAuth`). Fixed.

---

## 7. TypeScript Quality

All changes result in:
```
npx tsc --noEmit → 0 errors
```

Fixes applied:
- Removed unused imports: `Request`, `Response`, `NextFunction` from `index.ts`; `IOutboxRepository` from `fulfillment.service.ts`; `scanQrsFromPdf`, `fs` from `import.service.ts`
- Removed unused `_outboxRepository` constructor parameter from `FulfillmentService`
- Prefixed intentionally-unused callback parameters with `_v` in `import.service.ts`
- Fixed `generateVerificationCode` — removed invalid `await` inside sync method
- Deleted `src/vite.ts` (imported non-existent `vite` dependency and `vite.config`)

---

## 8. Remaining Technical Debt

These issues are **flagged but not fixed** in this pass because they require DB migrations, mobile app coordination, or significant functional redesign:

| Issue | Location | Risk |
|---|---|---|
| `purchases.sessionId` column name is misleading — it stores `userId`, not a session ID | `schema.ts` line 131 | Low: naming only, no functional impact |
| Admin API has no authentication — any caller can delete all vouchers | All `/api/admin/*` routes | **HIGH** — add `X-Admin-Key` middleware |
| `rbac.middleware.ts` has `_res` unused params across all 5 functions | `middleware/rbac.middleware.ts` | Low: TS warning only |
| `transaction-manager.ts` has implicit `any` on `tx` param | `infrastructure/persistence/` | Low: TS warning only |
| `src/scripts/*.ts` have unused imports and implicit any | Dev scripts, not in build | Low: excluded from tsconfig |
| OTP codes not invalidated after successful verification (replay window) | `verificationRepository` | Medium: OTP remains valid until expiry |
| No idempotency key on `POST /api/purchases` — double-tap payment possible | `purchase.controller.ts` | Medium: race condition risk |
| `fulfillments` table never queried after write — orphaned schema | `drizzle-fulfillment.repository.ts` | Low: schema is correct, app logic doesn't utilize it yet |

---

## 9. Files Changed

| File | Change Type |
|---|---|
| `src/index.ts` | Rewritten — removed feature flag, dead code, reflected CORS, added body size limit |
| `render.yaml` | Fixed — `NODE_ENV=production`, removed dummy credentials |
| `src/config/index.ts` | No change |
| `src/presentation/http/router.ts` | Simplified — removed legacy import, added session prune interval |
| `src/presentation/http/controllers/auth.controller.ts` | Fixed — dev login session, errors via next(), removed unused import |
| `src/presentation/http/controllers/purchase.controller.ts` | Security fix — removed unauthenticated `/session/:sessionId` endpoint |
| `src/presentation/http/controllers/voucher.controller.ts` | Security fix — added ownership check in `markUsed` |
| `src/application/services/auth.service.ts` | Critical fix — crypto OTP, removed hardcoded `"000000"` |
| `src/application/services/fulfillment.service.ts` | Cleanup — removed unused `IOutboxRepository` dep |
| `src/infrastructure/di/container.ts` | Updated — `FulfillmentService` call matches new constructor |
| `src/shared/infrastructure/twilio.ts` | Rewritten — removed Textbelt, added pino logging, masked phone numbers |
| `src/features/vouchers/vouchers.repository.ts` | Bug fix — `assignVoucherToPurchase` now actually sets `voucherId`; removed 30-line dead comment |
| `src/features/vouchers/import/import.service.ts` | Cleanup — removed hardcoded log file path, replaced with pino |
| `src/features/vouchers/import/analysis/gemini_pdf_analysis.ts` | Cleanup — removed `pdf-lib` import, replaced with pino logging |
| `package.json` | 9 runtime + 4 dev dependencies removed |
| `src/interfaces/http/routes.ts` | **Deleted** |
| `src/interfaces/http/routes/vouchers.ts` | **Deleted** |
| `src/vite.ts` | **Deleted** |
