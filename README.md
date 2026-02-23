# FuelFlow

A full-stack fuel voucher management platform. Users purchase fuel vouchers via a native mobile app; administrators import voucher inventory from PDF files (via AI-powered OCR) and manage the system through a web dashboard.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Repository Structure](#repository-structure)
3. [Component Breakdown](#component-breakdown)
4. [Data Flow](#data-flow)
5. [Database Schema](#database-schema)
6. [API Reference](#api-reference)
7. [Local Development Setup](#local-development-setup)
8. [Production Deployment (Render)](#production-deployment-render)
9. [Environment Variables](#environment-variables)
10. [Operational Concerns](#operational-concerns)
11. [Known Limitations & Risks](#known-limitations--risks)

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│                      Mobile App (Expo)                       │
│  React Native · Expo Router · TanStack Query · Zustand       │
│  iOS / Android — communicates via HTTPS                      │
└────────────────────────┬─────────────────────────────────────┘
                         │ REST / JSON
┌────────────────────────▼─────────────────────────────────────┐
│                   Admin Backend (Node.js)                    │
│  Express.js · TypeScript · Drizzle ORM                       │
│  Clean Architecture (Controllers → Services → Repositories)  │
├──────────────┬───────────────────────┬───────────────────────┤
│  PostgreSQL  │  Redis Streams        │  External APIs        │
│  (Supabase)  │  + DB Outbox Fallback │  Gemini AI / Twilio   │
└──────────────┴───────────────────────┴───────────────────────┘
                         │
┌────────────────────────▼─────────────────────────────────────┐
│               Fulfillment Consumer (in-process)              │
│  Subscribes to order events, assigns vouchers FIFO           │
└──────────────────────────────────────────────────────────────┘
                         │ (Admin manages via)
┌────────────────────────▼─────────────────────────────────────┐
│               Admin Frontend (React SPA)                     │
│  Vite · Tailwind CSS v4 · TanStack Query                     │
│  Voucher import, station/package management, QR viewer       │
└──────────────────────────────────────────────────────────────┘
```

**Key design decisions:**
- The backend is the single source of truth. Both the mobile app and the admin frontend are pure API clients.
- Orders are decoupled from voucher availability. A purchase can succeed even with zero inventory; when vouchers are imported later, the `FulfillmentConsumer` automatically backfills pending orders.
- Session cookies carry authentication state. They are persisted in PostgreSQL (`sessions` table) so they survive backend restarts.

---

## Repository Structure

```
FuelFlow/
├── admin-panel/
│   ├── backend/              # Node.js/Express API
│   │   └── src/
│   │       ├── application/services/   # Business logic (PurchaseService, AuthService, VoucherService)
│   │       ├── config/                 # Centralized env/config
│   │       ├── domain/repositories/    # Repository interfaces (ports)
│   │       ├── features/              # Legacy feature modules (still active)
│   │       │   ├── orders/            # Order creation & outbox
│   │       │   ├── vouchers/          # Voucher CRUD & PDF import pipeline
│   │       │   ├── stations/          # Stations, fuel types, packages
│   │       │   └── users/             # User & verification management
│   │       ├── infrastructure/        # Concrete implementations (Drizzle repos, DI container, logger)
│   │       ├── interfaces/http/       # Legacy route file (partially active alongside new router)
│   │       ├── presentation/http/     # New Clean Architecture router + controllers
│   │       ├── services/              # fulfillment.consumer.ts (singleton in-process service)
│   │       └── shared/                # DB schema, Redis client, encryption, errors
│   ├── frontend/             # React Admin Dashboard (Vite + Tailwind v4)
│   └── database/             # Standalone drizzle config (rarely used)
├── mobile-app-native/        # Expo React Native app
│   ├── app/                  # Expo Router screens
│   │   ├── index.tsx          # Landing / home
│   │   ├── landing.tsx        # Auth gate
│   │   ├── packages.tsx       # Browse fuel packages
│   │   ├── basket.tsx         # Shopping cart
│   │   ├── checkout.tsx       # Payment flow
│   │   ├── my-codes.tsx       # User's vouchers & pending orders
│   │   ├── map.tsx            # Station map
│   │   └── profile.tsx        # User profile
│   └── src/
│       ├── components/        # Reusable UI components
│       ├── hooks/             # useAuth, useStations
│       ├── lib/               # api.ts, store.ts, design-tokens, i18n, themes
│       └── shared/
├── docker-compose.yml         # Full local stack
├── render.yaml                # Render.com deployment config
└── .env.example               # Template for required environment variables
```

---

## Component Breakdown

### Admin Backend

**Runtime entry:** `src/index.ts`

The backend selects its routing mode via the `USE_REFACTORED_ARCHITECTURE=true` environment variable:
- **`true` (current production default):** Uses `src/presentation/http/router.ts` — Clean Architecture with DI container, PostgreSQL session store, pino structured logging.
- **`false` / unset:** Falls back to `src/interfaces/http/routes.ts` — a legacy monolithic routes file. Both share the same database schema and Drizzle repositories.

**Key services:**

| Service | File | Responsibility |
|---|---|---|
| `AuthService` | `application/services/auth.service.ts` | OTP send/verify, phone normalization, user find-or-create |
| `PurchaseService` | `application/services/purchase.service.ts` | Checkout creation, payment simulation, order splitting |
| `VoucherService` | `application/services/voucher.service.ts` | Voucher CRUD, user voucher retrieval, inventory |
| `FulfillmentConsumer` | `services/fulfillment.consumer.ts` | Async voucher-to-order assignment (runs in-process) |
| `ImportOrchestrator` | `features/vouchers/import/import.service.ts` | PDF → image → Gemini OCR → voucher creation pipeline |

**Architecture layers (Clean side):**

```
HTTP Request
    └─▶ Controller (router.ts + controllers/)
            └─▶ Service (application/services/)
                    └─▶ Repository Interface (domain/repositories/)
                                └─▶ Drizzle Impl (infrastructure/persistence/)
                                            └─▶ PostgreSQL
```

**Authentication flow:**

1. `POST /api/auth/phone/send-code` — normalizes phone to `+380XXXXXXXXX`, generates OTP (`000000` always in current config), stores in `phone_verifications` table.
2. `POST /api/auth/phone/verify` — validates OTP, calls `req.session.regenerate()`, sets `session.userId` + `session.phoneAuth = true`, then `req.session.save()` before responding. Session is stored in PostgreSQL.
3. `GET /api/auth/phone/user` — reads `session.userId` + `session.phoneAuth` to identify user.

> **Note:** The OTP is hardcoded to `000000` in `AuthService.generateVerificationCode()`. This is intentional for the current development/demo stage. Production use requires proper random OTP generation.

**Session cookie configuration (production):**

```
Secure: true      (HTTPS only)
HttpOnly: true
SameSite: none    (required for cross-origin mobile requests)
MaxAge: 24h
Store: PostgreSQL (connect-pg-simple)
```

---

### Admin Frontend

A single-page React app built with Vite and Tailwind CSS v4.

**Tabs / features:**
- **Stations** — CRUD for fuel stations (name, color, logo text)
- **Fuel Types** — create/edit fuel types per station with base and discount pricing
- **QR Codes** — legacy manual QR code entry (largely superseded by the import pipeline)
- **Packages** — define saleable fuel packages; "suggested packages" are auto-generated from imported voucher data
- **Vouchers** — paginated, filterable table of all vouchers with bulk actions (delete, activate, expire, assign)
- **Purchases** — view all purchase records
- **Import** — drag-and-drop PDF upload, triggers the OCR pipeline, shows job status

The frontend proxies all `/api` requests to `http://localhost:4000` during development via Vite's dev server.

---

### Mobile App (Expo React Native)

Built with Expo SDK and Expo Router for file-based routing.

**Authentication state management:**

Two sources of truth are synchronized automatically:
- **`useAuth` hook** — queries `GET /api/auth/phone/user` (server authoritative)
- **Zustand store** — `isAuthenticated` boolean (local, optimistic)

`AuthSync` component in `_layout.tsx` reconciles these: if the server says no valid session but the local store thinks authenticated, it forces logout and redirects to `/landing`.

**Purchase flow:**

1. User browses packages (`/packages`) and adds items to cart (Zustand store).
2. Cart review at `/basket`, then proceeds to `/checkout`.
3. `handlePaymentEnd` in `checkout.tsx` iterates over cart items and for each calls:
   - `POST /api/purchases` — creates a purchase record (`status: pending`)
   - `POST /api/purchases/simulate` — simulates payment success, which creates `PENDING_FULFILLMENT` orders (one per voucher quantity unit)
4. On success: cart cleared, redirected to `/my-codes`.

**My Codes screen:**

Fetches both:
- `GET /api/vouchers/my` — fulfilled vouchers assigned to the user (`assignedToUserId = userId`)
- `GET /api/sync/orders` — orders filtered by `userId`, showing pending/fulfilled status

Vouchers can be toggled as "used" or "restored" with optimistic in-app state.

---

## Data Flow

### Purchase & Fulfillment

```
User buys voucher
        │
        ▼
POST /api/purchases          →  creates purchase record (status=pending)
        │
POST /api/purchases/simulate →  sets purchase status=paid
                                creates N orders in `orders` table (status=PENDING_FULFILLMENT)
                                publishes ORDER_CREATED to Redis Stream (or outbox)
        │
        ▼
FulfillmentConsumer (async)
        │
        ├── reads from Redis Stream  (real-time)
        │   OR polls `outbox` table  (fallback every 5s)
        │
        ▼
findAndAssignVoucher()
        ├── SELECT ... FROM vouchers WHERE status='available' AND provider/fuelType/amount match
        ├── FOR UPDATE SKIP LOCKED  (row-level lock, race-condition safe)
        ├── UPDATE vouchers SET assignedToUserId, status='sold'
        └── INSERT INTO fulfillments (orderId, voucherId)
        │
UPDATE orders SET status='FULFILLED'
        │
Mobile app polls /api/sync/orders + /api/vouchers/my  →  user sees voucher
```

### Voucher Import (OCR Pipeline)

```
Admin uploads PDF(s)
        │
POST /api/vouchers/import
        │
ImportOrchestrator.queueJob()   (in-memory queue, sequential processing)
        │
For each PDF file:
  1. convertPdfToImages()        pdfjs-dist renders pages to PNG buffers
  2. scanQrsFromPdf()            jsqr scans for machine-readable QR codes
  3. analyzeVoucherImage()       Google Gemini AI extracts: provider, fuelType, amount, externalId
  4. encryptionService.encrypt() AES encrypts the raw QR code data before storage
  5. vouchersRepository.createVoucher()  inserts to DB (idempotent via externalId unique index)
  6. ordersRepository.publishEvent(VOUCHERS_IMPORTED)  triggers backfill of pending orders
        │
FulfillmentConsumer handles VOUCHERS_IMPORTED
        └── getPendingOrdersByProductType() → fulfills matching pending orders
```

---

## Database Schema

All tables live in a single PostgreSQL database. Schema source: `admin-panel/backend/src/shared/database/schema.ts`.

| Table | Purpose |
|---|---|
| `users` | User accounts; keyed by UUID; stores phone (unique), email (unique), referral data |
| `sessions` | Express session store (connect-pg-simple) |
| `phone_verifications` | OTP records; `code` is always `000000` in current config |
| `stations` | Fuel station brands (OKKO, WOG, UPG, Shell, etc.) |
| `station_nodes` | Individual physical station locations with lat/lng |
| `fuel_types` | Fuel type definitions per station with base and discount pricing |
| `fuel_packages` | Saleable packages (station + fuel type + liters + price) |
| `vouchers` | Voucher inventory; `qrCodeData` is AES-encrypted; `assignedToUserId` links to user after fulfillment |
| `purchases` | Purchase transaction records; `sessionId` = userId |
| `orders` | Fulfillment tracking; one row per voucher unit; `status` = `PENDING_FULFILLMENT` → `FULFILLED` |
| `fulfillments` | Junction table linking orders to vouchers |
| `import_jobs` | Tracks async PDF import job status |
| `outbox` | Transactional outbox events for reliable async processing |
| `notifications` | Per-user notification records |
| `qr_codes` | Legacy manual QR code inventory (mostly superseded by `vouchers`) |

---

## API Reference

### Auth

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/phone/send-code` | Send OTP to phone number |
| `POST` | `/api/auth/phone/verify` | Verify OTP, establish session |
| `GET` | `/api/auth/phone/user` | Get current authenticated user |
| `POST` | `/api/auth/phone/logout` | Destroy session |

### Purchases & Checkout

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/purchases` | ✅ | Create purchase record |
| `GET` | `/api/purchases/my` | ✅ | Get current user's purchases |
| `POST` | `/api/purchases/simulate` | ✅ | Simulate payment (dev mode) |
| `POST` | `/api/checkout` | ✅ | Legacy checkout endpoint |

### Vouchers (User)

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/vouchers/my` | ✅ | Get current user's assigned vouchers |
| `PATCH` | `/api/vouchers/:id/mark-used` | ✅ | Mark a voucher as used |
| `PATCH` | `/api/vouchers/:id/restore` | ✅ | Restore a used voucher |

### Sync (Mobile)

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/sync` | ✅ | Get user's orders + vouchers combined |
| `GET` | `/api/sync/orders` | ✅ | Get user's orders |

### Public

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/stations` | List all stations |
| `GET` | `/api/station-nodes` | List all station physical locations |
| `GET` | `/api/packages` | List all fuel packages |
| `GET` | `/api/inventory` | Aggregated voucher inventory |
| `GET` | `/api/health` | Health check |

### Admin

| Method | Path | Description |
|---|---|---|
| `GET/POST/PUT/DELETE` | `/api/admin/stations` | Station management |
| `GET/POST/PUT/DELETE` | `/api/admin/fuel-types` | Fuel type management |
| `GET/POST/PUT/DELETE` | `/api/admin/packages` | Package management |
| `GET/POST/DELETE` | `/api/admin/qr-codes` | Legacy QR code management |
| `GET` | `/api/admin/purchases` | All purchases |
| `GET` | `/api/admin/vouchers` | Paginated voucher list with filters |
| `POST` | `/api/admin/vouchers/bulk-action` | Bulk: delete, activate, expire, assign |
| `POST` | `/api/vouchers/import` | Upload PDFs for OCR import |
| `GET` | `/api/vouchers/import-status/:id` | Import job status |
| `POST` | `/api/test/assign-vouchers` | Manually assign vouchers to user |

---

## Local Development Setup

### Prerequisites

- Node.js 20+
- Docker & Docker Compose
- A PostgreSQL database (or use the Docker Compose stack)
- Optional: Redis (the backend falls back to DB polling without it)

### Full Stack via Docker Compose

```bash
# 1. Copy and fill environment variables
cp .env.example .env
# Edit .env with your values (see Environment Variables section)

# 2. Start everything
docker-compose up --build
```

Services available:
- **Backend API:** http://localhost:4000
- **Admin Frontend:** http://localhost:5002
- **Mobile App (web preview):** http://localhost:5003

### Backend Only (local dev)

```bash
cd admin-panel/backend
cp .env.example .env       # Fill in DATABASE_URL, etc.
npm install
npm run dev                # tsx watch (hot reload)
```

### Admin Frontend Only

```bash
cd admin-panel/frontend
npm install
npm run dev                # Vite dev server on :5173
                           # Proxies /api → localhost:4000
```

### Mobile App

```bash
cd mobile-app-native
npm install

# Set the API URL
echo "EXPO_PUBLIC_API_URL=https://fuel-flow-admin-panel-bac.onrender.com" > .env
# Or for local backend:
# echo "EXPO_PUBLIC_API_URL=http://localhost:4000" > .env

# Run on iOS simulator
npm start -- --ios

# Run on Android
npm start -- --android
```

> **iOS Physical Device Note:** The device must be able to reach the backend URL. Local `localhost:4000` is not accessible from a physical device. Use the production URL or the machine's LAN IP (e.g., `http://192.168.x.x:4000`), and ensure cookies work correctly (requires HTTPS for `SameSite=None; Secure`).

### Database Migrations

The schema is managed via Drizzle Kit:

```bash
cd admin-panel/backend
npm run db:push    # Push schema changes to the database (no migration files)
```

---

## Production Deployment (Render)

The backend is deployed to [Render](https://render.com) via `render.yaml`.

**Service:** `fuel-admin-backend`
- **Build:** `npm install && npm run build` (esbuild bundles to `dist/index.js`)
- **Start:** `npm start` → runs `npm run db:push && node dist/index.js`
- **Root directory:** `admin-panel/backend`

**Required secrets to set in Render Dashboard (not in render.yaml):**

| Key | Description |
|---|---|
| `DATABASE_URL` | PostgreSQL connection string (Supabase or any PG) |
| `REDIS_URL` | Redis connection string (optional; system degrades gracefully) |
| `GEMINI_API_KEY` | Google AI Studio API key for OCR |
| `QR_ENCRYPTION_KEY` | 64-char hex key for AES-256 QR data encryption |
| `TWILIO_ACCOUNT_SID` / `TWILIO_AUTH_TOKEN` / `TWILIO_PHONE_NUMBER` | SMS (currently OTP is always `000000`, so not strictly required) |

`SESSION_SECRET` is auto-generated by Render on first deploy.

**Current live backend:** `https://fuel-flow-admin-panel-bac.onrender.com`

---

## Environment Variables

Complete reference for `admin-panel/backend/.env`:

```env
# ─── Database ───────────────────────────────────────────
DATABASE_URL=postgresql://user:password@host:port/dbname

# ─── Application ────────────────────────────────────────
NODE_ENV=development            # 'production' disables dev fallbacks
PORT=4000
SESSION_SECRET=change-me        # Min 32 chars random string in production
USE_REFACTORED_ARCHITECTURE=true   # Always set to true

# ─── Redis (optional) ────────────────────────────────────
REDIS_URL=redis://localhost:6379   # Omit to disable Redis (outbox fallback used)

# ─── Voucher QR Encryption ──────────────────────────────
QR_ENCRYPTION_KEY=fa5dbc...     # 64-char hex (openssl rand -hex 32)

# ─── Google Gemini AI (for PDF import OCR) ──────────────
GEMINI_API_KEY=AIzaSy...
GEMINI_MODEL=gemini-2.0-flash-exp   # Optional, this is the default

# ─── Twilio SMS ─────────────────────────────────────────
TWILIO_ACCOUNT_SID=ACxxxxxxxx
TWILIO_AUTH_TOKEN=xxxxxxxx
TWILIO_PHONE_NUMBER=+1234567890

# ─── Stripe (payment processing, partially implemented) ──
STRIPE_SECRET_KEY=sk_test_...
STRIPE_PUBLISHABLE_KEY=pk_test_...
STRIPE_WEBHOOK_SECRET=whsec_...
```

Mobile app (`mobile-app-native/.env`):

```env
EXPO_PUBLIC_API_URL=https://fuel-flow-admin-panel-bac.onrender.com
```

---

## Operational Concerns

### Logging

The backend uses **pino** with structured JSON output. Each log entry includes `component`, `method`, `path`, `statusCode`, `duration`, and correlation/request IDs added by middleware.

In development, `pino-pretty` formats output for readability. In production on Render, logs are available in the Render dashboard.

### Authentication & Sessions

- Sessions expire after **24 hours** (`SESSION_MAX_AGE_MS`).
- Sessions are stored in the `sessions` PostgreSQL table and survive backend restarts/redeployments.
- Switching between local and production backends will invalidate sessions (different `SESSION_SECRET`).
- In `NODE_ENV !== 'production'`, the backend falls back to a hardcoded dev user ID (`d366f82a-e65c-4110-bf20-ab2f44750cfe`) for unauthenticated requests to protected routes. **This must not appear in production.**

### Phone Number Normalization

The auth service normalizes all Ukrainian phone numbers to E.164 format (`+380XXXXXXXXX`) before storing or looking up users:
- `0671234567` → `+380671234567`
- `380671234567` → `+380671234567`
- `+380671234567` → `+380671234567` (no change)

Inconsistent phone formats across logins will create separate user accounts.

### Fulfillment Consumer

The `FulfillmentConsumer` runs as an in-process singleton within the backend process. On startup it tries Redis Streams; if unavailable, it polls the `outbox` table every 5 seconds.

- Redis mode: real-time, uses consumer groups with `SKIP LOCKED` for concurrency safety.
- Outbox mode: ~5s latency, processed in batches of 10.
- Voucher assignment uses `SELECT ... FOR UPDATE SKIP LOCKED` to prevent double-assignment under concurrent load.

### Rate Limiting

Built-in in-memory rate limiting (no external dependency needed):
- OTP send: 3 requests/minute per IP
- OTP verify: 5 requests/5 minutes per IP + 3 attempts per phone number

### Scaling

- The backend is **stateful** (in-memory rate limiter, in-memory ImportOrchestrator job queue, in-process FulfillmentConsumer). Running multiple backend instances will cause issues.
- For horizontal scaling: externalize the rate limiter to Redis, move the import job queue to a proper task queue (BullMQ), and ensure only one `FulfillmentConsumer` instance runs (use Redis consumer groups with a single consumer name).

---

## Known Limitations & Risks

| Area | Issue |
|---|---|
| **OTP** | Always returns `000000`. Any phone number can authenticate with code `000000`. Acceptable for demo/internal use only. |
| **Dual routing** | Two route files are active simultaneously (`routes.ts` + `router.ts`). Routes in `routes.ts` take priority for paths registered there (e.g., `/api/auth/phone/...`, `/api/vouchers/my`). This creates potential confusion and duplicate logic. |
| **Single-instance only** | Rate limiter, import queue, and fulfillment consumer are all in-process. No horizontal scaling without refactoring. |
| **Admin authentication** | The admin frontend has no authentication. Anyone who can reach port 5002 has full admin access. |
| **Stripe integration** | Stripe code exists but payments are simulated. The `/api/purchases/simulate` endpoint is used instead of real payment processing. |
| **Session cross-device** | Physical mobile devices require HTTPS and `SameSite=None; Secure` cookies. Local development with `http://localhost` will not work from physical devices. |
| **Duplicate user accounts** | If a user logs in with the same phone number in different formats (e.g., typo), separate user records are created. No account merging mechanism exists. |
| **QR encryption key rotation** | Changing `QR_ENCRYPTION_KEY` will permanently break decryption of existing voucher QR data in the database. |
| **Import queue** | ImportOrchestrator stores the job queue in memory. A backend restart while processing will lose queue state (job record will remain in `processing` status indefinitely). |
