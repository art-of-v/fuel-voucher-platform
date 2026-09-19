# FuelFlow

A full-stack fuel-voucher platform. End users buy fuel (in liters) at partner stations
(OKKO, WOG, KLO) from a native mobile app, pay via Monobank, and receive digital vouchers
they redeem at the pump. Administrators import physical voucher stock from PDF catalogs,
manage pricing and inventory, and reconcile payments against fuel delivered — all from a
web dashboard.

Three applications share one backend:

| App | Stack | Purpose |
|---|---|---|
| [`mobile/`](mobile) | React Native 0.81 · Expo 54 · Expo Router · NativeWind · Zustand · TanStack Query | End-user app: map, stations, packages, basket, checkout, payments, vouchers, profile |
| [`admin/`](admin) | React 19 · Vite 7 · TypeScript · shadcn/ui · Tailwind 4 · TanStack Query · Zustand · Recharts | Back-office: voucher import, providers/pricing, users, contracts, reports, audit & error logs |
| [`backend/`](backend) | .NET 10 · ASP.NET Core · EF Core 10 · PostgreSQL (Npgsql) · Hangfire · Redis | REST API, background jobs, Monobank integration, auth, outbox |

---

## Table of Contents

1. [Architecture](#architecture)
2. [Repository structure](#repository-structure)
3. [Key flows](#key-flows)
4. [Money & currency](#money--currency)
5. [Database schema](#database-schema)
6. [API reference](#api-reference)
7. [Local development](#local-development)
8. [Environment variables](#environment-variables)
9. [Deployment](#deployment)
10. [Documentation index](#documentation-index)
11. [Known limitations](#known-limitations)

---

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                     Mobile App (Expo)                          │
│  React Native · Expo Router · NativeWind · TanStack Query      │
│  iOS / Android / web — communicates via HTTPS (JWT bearer)     │
└────────────────────────┬─────────────────────────────────────┘
                         │ REST / JSON
┌────────────────────────▼─────────────────────────────────────┐
│                    Backend API (.NET 10)                       │
│  ASP.NET Core · EF Core 10 · Npgsql · Hangfire · Redis         │
│  Controllers → Command/Query handlers → DbContext (CQRS-lite)  │
├──────────────┬───────────────────────┬───────────────────────┤
│  PostgreSQL  │  Hangfire (in-process │  External APIs         │
│   (server)   │  + outbox dispatch)   │  Monobank / SMS Club   │
└──────────────┴───────────────────────┴───────────────────────┘
                         ▲
                         │ REST / JSON (JWT + Admin role)
┌────────────────────────┴─────────────────────────────────────┐
│                 Admin Frontend (React 19 SPA)                  │
│  Vite · Tailwind 4 · shadcn/ui · TanStack Query                │
│  Import, providers, users, contracts, reports, audit logs      │
└──────────────────────────────────────────────────────────────┘
```

**Key design decisions:**
- The backend is the single source of truth. Both the mobile app and the admin panel are pure API clients.
- Orders are decoupled from voucher availability. A purchase can succeed with zero inventory; when vouchers are imported later, the fulfillment job automatically backfills pending orders (FEFO — First Expiry, First Out).
- Money-affecting values (checkout price, webhook amount, order status) are computed and enforced server-side; the client only ever *proposes*.
- Background jobs run **in-process** via Hangfire (PostgreSQL storage) by default; the same jobs can also run in the standalone `FuelFlow.JobsWorker` for horizontal scaling.
- Domain events use an **outbox**: events are written in the same transaction as the state change, then dispatched to consumers (fulfillment, notifications) by a recurring job.
- JWT bearer tokens carry identity; admin endpoints require the `Admin` role claim; a per-request middleware re-checks the session against the DB so revocation is immediate (see [docs/SECURITY.md](docs/SECURITY.md)).

### Backend layout

```
backend/src/
├── FuelFlow.API/                  # Web API — controllers, handlers, EF, Hangfire jobs
│   ├── Program.cs                 # Bootstrap: Serilog, DI, Hangfire, migration-on-boot
│   ├── Extensions/                # ServiceSetup, PipelineSetup, AuthSetup, DatabaseSetup
│   ├── Middleware/                # RequestLogging, GlobalExceptionHandler, SessionValidation, DeviceSignature
│   ├── Persistence/               # ApplicationDbContext, SeedData
│   ├── SharedKernel/              # Domain entities, Money, Options, JwtTokenService
│   ├── BackgroundJobs/            # FulfillmentService, NotificationService, refund sync
│   ├── Migrations/                # EF Core migrations (canonical schema)
│   └── Features/
│       ├── Auth/                  # OTP login, JWT refresh, device registration, admin users
│       ├── Orders/                # Checkout (single + bulk), Monobank invoices, fulfillment, outbox
│       ├── Vouchers/              # Import (PDF/QR), catalog, inventory, user vouchers, verification
│       ├── Company/               # Company owner/worker: invitations, membership, gift/recall
│       ├── Providers/ & Stations/ # Fuel packages, per-liter pricing (supplier/margin/final)
│       ├── Contracts/             # Legal entities, client contracts & digital signatures
│       ├── Monobank/              # Signed payment webhook
│       ├── Report/                # Per-user profit/spending reports
│       ├── Admin/                 # Dashboard, reconciliation, admin voucher/user/order ops
│       ├── Audit/ & ErrorLogs/    # audit_log + error_logs tables and admin UIs
│       ├── Notifications/ Referral/ Sync/ Settings/
│       └── ...
└── FuelFlow.JobsWorker/           # Standalone Hangfire worker (jobs also run in-process in the API)
```

**Patterns:**
- **CQRS-lite** — each endpoint maps to a `*Command`/`*Query` + `*Handler`; read paths use `AsNoTracking()`, writes are explicitly tracked.
- **Outbox** — `OrderCreated` / `OrderFulfilled` events are persisted to `outbox_events` in the write transaction and dispatched by the recurring job (or `FuelFlow.JobsWorker`).
- **API-first** — one backend serves the mobile app, the admin panel, and Monobank webhooks.

### Request pipeline

```
ForwardedHeaders → RequestLogging → ExceptionHandler → CORS → ResponseCaching
  → Authentication → RateLimiter → SessionValidation → Authorization → DeviceSignature → Controllers
```

The relative order of the security middlewares is pinned by
`PipelineOrderConventionTests` so a refactor cannot silently turn per-user rate limits
into per-IP ones.

`SessionValidationMiddleware` runs on every authenticated request: it 401s if the user
row is missing/inactive or if the signed `token_version` claim differs from the DB.
`DeviceSignatureMiddleware` enforces an asymmetric device signature on the endpoints
listed in `DeviceAuth:RequireSignatureForEndpoints` (checkout).

**Observability:** Serilog logs to the console; a `DatabaseLoggerProvider` asynchronously
persists `Error`/`Critical` records to `error_logs` (surfaced in the admin **Error Logs**
tab), and `RequestLoggingMiddleware` records every request (method, path, origin, status,
duration, IP). Domain/admin changes are written to the `audit_log`.

---

## Repository structure

```
FuelFlow/
├── admin/                    # React 19 admin dashboard (Vite + Tailwind 4 + shadcn/ui)
├── backend/                  # .NET 10 ASP.NET Core API + standalone jobs worker
│   └── src/
│       ├── FuelFlow.API/
│       └── FuelFlow.JobsWorker/
├── mobile/                   # Expo React Native app
│   ├── app/                  # Expo Router screens (index, landing, packages, basket,
│   │                         #   checkout, my-codes, map, profile, company/…)
│   └── src/                  # components, hooks, features, core (api, i18n, store)
├── deploy/                     # Hetzner production stack (compose, Caddyfile, backup/restore)
├── docs/                     # Documentation (see the Documentation index below)
├── docker-compose.yml        # Full local stack
└── .env.example              # Template for required environment variables
```

---

## Key flows

### 1. Authentication (phone OTP + device binding)

1. User enters a phone number → `POST /api/auth/send-code`. In dev the code is always
   `000000` (`FakeSmsService`); in production a random code is sent via SMS Club. This is
   controlled by the **`Auth:DevBypass`** flag, not the environment profile.
2. `POST /api/auth/verify` validates the code and returns a JWT **access** token + a
   rotating **refresh** token (also set as an `HttpOnly` cookie scoped to `/api/auth/refresh`).
   Access tokens live **15 min** in production (200 min in dev); refresh tokens live **7 days**.
   New phone numbers auto-create a user; deactivated/soft-deleted users are rejected.
3. Clients refresh transparently via `POST /api/auth/refresh` (refresh-token rotation with
   family reuse detection).
4. The mobile app registers a device keypair and answers a server **challenge** with a
   signature (`/api/auth/device/*`); biometric/PIN unlock is a local gate. The device
   signature protects money-moving endpoints (checkout).

Admins use the **same** login flow; the `Admin` role is granted by setting the user's
`role_id` in the DB (see [docs/MANUAL_TESTING.md](docs/MANUAL_TESTING.md)).

### 2. Voucher import (admin)

1. Admin uploads a PDF catalog: `POST /api/voucher-catalog/import` (multipart, field `file`; ~300 s client timeout).
2. The backend rasterizes each page with the Skia-based `PdfRenderer` (200 DPI), decodes the
   QR with a custom decoder, and parses the payload with the brand-specific parser (OKKO / WOG / KLO).
3. Each voucher is **verified** (decoded QR ↔ printed data, with a confidence/mismatch %) and
   **deduplicated globally** by voucher number *or* QR payload across the whole table.
4. Results (`imported` / `duplicates` / `failed` / `verificationFailed`) are returned and shown
   in the admin **Import** tab; batch history is retained.

### 3. Purchase / checkout (mobile)

1. User picks a station → selects fuel packages (liters × quantity) → basket.
2. `POST /api/purchases` (or `POST /api/purchases/bulk`), device-signed, creates an order
   (`PendingPayment`) and a Monobank invoice, returning a payment URL. **Price is recomputed
   server-side from `FuelPackages`** — the client value is only a proposal.
3. User pays in Monobank → Monobank calls `POST /api/monobank/webhook`. The signature
   (`X-Sign`, ECDSA over the raw body) is **verified and fail-closed (401)** and the amount is
   checked; on success the order → `PendingFulfillment` and an `OrderCreated` outbox event is queued.
   (In dev, `POST /api/purchases/simulate` — Admin — stands in for the webhook.)
4. The Hangfire `FulfillmentService` (`*/1 * * * *`) assigns `Available` vouchers matching
   `(provider, fuelTypeId, liters)` via **FEFO**; the order → `Fulfilled` (or `PartiallyFulfilled`
   if stock is short). `NotificationService` then creates an in-app notification.
5. The app polls `GET /api/vouchers/my` + `GET /api/sync/orders`; redemption at the pump is
   self-reported via `PATCH /api/vouchers/{id}/mark-used`.

### 4. Reconciliation, refunds & reports (admin)

- Reconciliation compares paid orders vs. delivered vouchers vs. imported stock; the money
  ledger is `received = delivered value + refunded + outstanding`.
- Partial orders can be refunded (manual `POST /api/admin/orders/{id}/refund` or the optional
  auto-refund job) for the undelivered value via Monobank invoice cancellation; a `sync-refund-status`
  job reconciles refund state. See [docs/RECONCILIATION.md](docs/RECONCILIATION.md).
- **Profit** is earned margin on liters actually delivered (`FuelPackage.MarginUahPerLiter × liters × delivered qty`).

### 5. Company workers

A user who creates a `LegalEntity` becomes a company **owner**: they can buy vouchers for the
company (`legalEntityId` on checkout), invite registered users as **workers**, gift/recall
company vouchers, and fire workers (which blocks their gifted vouchers). See
[docs/COMPANY_WORKERS.md](docs/COMPANY_WORKERS.md).

---

## Money & currency

**Convention: the domain model stores money in whole-UAH integers; kopecks exist only at the
Monobank boundary.**

| Unit | Where |
|---|---|
| UAH (int) | `orders.price`, `order_line_items.unit_price` / `line_total`, `fuel_packages.price` / `original_price`, `fuel_types.base_price` / `discount_price` (per liter) |
| kopecks (int) | `refunds.amount`; every Monobank API call (`invoice/create`, `invoice/cancel`, webhooks) and any DTO field explicitly named `...Kopecks` |

Monobank's merchant API mandates kopeck integers, and business prices are whole UAH, so
keeping the domain in UAH avoids floating-point money math without paying kopeck-conversion
cost everywhere.

Rules:

1. Every UAH↔kopeck conversion goes through `FuelFlow.SharedKernel.Money` (`ToKopecks` /
   `FromKopecks`). A bare `* 100` / `/ 100` is a unit-mismatch suspect in review — that exact
   mistake once shipped kopecks into `fuel_types.base_price` and rendered "8492.00 ₴/L".
2. Frontends receive kopecks only from `...Kopecks` fields and divide by 100 at display time;
   everything else is UAH.
3. If fractional-UAH pricing is ever required, migrate the domain columns to kopecks
   deliberately (data migration + all display sites) rather than mixing units.

---

## Database schema

The schema is a single PostgreSQL database managed by **EF Core migrations** in
`backend/src/FuelFlow.API/Migrations/` — the migrations are the canonical source of truth.
The primary tables:

| Table | Purpose |
|---|---|
| `users` | Accounts (UUID, unique phone, email, role); `is_active`, `token_version`, `is_deleted` drive session revocation & soft-delete |
| `phone_verifications` | OTP records |
| `devices` | Registered device public keys for challenge/signature auth |
| `refresh_tokens` | Rotating refresh tokens (`family_id` for reuse detection) |
| `stations` / `station_nodes` | Fuel brands and individual physical locations (lat/lng) |
| `fuel_types` | Fuel-type definitions per station with base/discount pricing (per liter, UAH) |
| `fuel_packages` | Saleable packages (station + fuel type + liters + price, UAH); carry supplier/margin/final pricing |
| `fuel_vouchers` | Voucher inventory; `qr_image` rendered from `qr_parameters`; carries `legal_entity_id` + `worker_user_id` |
| `qr_parameters` | QR encoding config (version, ECC level, mask, encoding mode) |
| `orders` / `order_line_items` | Purchase orders and their line items (UAH) |
| `fulfillments` | Junction linking orders to the vouchers that satisfy them |
| `refunds` | Refund records (amount in **kopecks**; one per order) |
| `voucher_imports` / `voucher_import_errors` | Batch PDF import jobs + per-voucher failures |
| `legal_entities` | Company profiles (owner = the user who created one) |
| `company_invitations` / `company_members` | Owner↔worker invitations and membership |
| `outbox_events` | Transactional event log dispatched by background jobs |
| `notifications` | In-app user notifications |
| `audit_log` / `error_logs` | Admin/domain audit trail and persisted error records |
| `contracts` / `referrals` | Client contracts (digital signatures) and referral records |

---

## API reference

All routes are prefixed `/api`. Auth column: ✅ = JWT bearer required; **Admin** = `Admin`
role required; — = public. This lists the primary endpoints; admin sub-resources follow the
`/api/admin/<resource>` convention.

### Auth & users

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/auth/send-code` | — | Send OTP to a phone number (mobile; auto-registers unknown phones) |
| `POST` | `/api/auth/verify` | — | Verify OTP → access + refresh tokens |
| `POST` | `/api/auth/admin/send-code` | — | Admin login: send OTP **only** to an existing staff account (no-op, no leak, otherwise) |
| `POST` | `/api/auth/admin/verify` | — | Admin login: verify OTP for staff only; never auto-registers |
| `POST` | `/api/auth/refresh` | — | Rotate tokens (body or `refresh_token` cookie) |
| `GET` | `/api/auth/user/me` | ✅ | Current authenticated user |
| `POST` | `/api/auth/device/{register,challenge,verify,logout}` | mixed | Device registration & challenge/signature auth |
| `DELETE` | `/api/users/me` | ✅ | Soft-delete own account |
| `DELETE` | `/api/admin/users/{id}` | Admin | Soft-delete a user |

### Purchases & orders

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/purchases` | ✅ (device-signed) | Create a single purchase + Monobank invoice |
| `POST` | `/api/purchases/bulk` | ✅ (device-signed) | Create a multi-item purchase |
| `GET` | `/api/purchases/my` | ✅ | Current user's purchases (with vouchers) |
| `POST` | `/api/purchases/simulate` | Admin | Simulate a Monobank callback (dev/testing) |
| `POST` | `/api/monobank/webhook` | — (signed) | Monobank payment callback — signature-verified, fail-closed 401 |
| `GET` | `/api/sync` · `/api/sync/orders` | ✅ | Combined sync (orders + totals) / user's orders |

### Vouchers

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/vouchers/my` | ✅ | User's vouchers (plain array; includes `source`, `legalEntityId`, `workerUserId`) |
| `PATCH` | `/api/vouchers/{id}/mark-used` | ✅ | Mark a voucher used (worker-guarded for gifted vouchers) |
| `PATCH` | `/api/vouchers/{id}/restore` | Admin | Restore a used voucher |
| `GET` | `/api/vouchers/inventory` | Admin | Aggregated inventory by provider/fuel/liters |
| `POST` | `/api/voucher-catalog/import` | Admin | Upload a PDF catalog (multipart, field `file`) |
| `GET` | `/api/voucher-catalog` | Admin | Voucher catalog list |
| `GET` | `/api/voucher-catalog/{id}/qr` | ✅ | Render a voucher's QR (holder or Admin) |

### Company (owner/worker)

| Method | Path | Actor | Description |
|---|---|---|---|
| `POST` `GET` | `/api/company/invitations` | Owner | Send / list sent invitations |
| `DELETE` | `/api/company/invitations/{id}` | Owner | Cancel a pending invitation |
| `GET` | `/api/company/my-invitations` | Worker | List received invitations |
| `POST` | `/api/company/invitations/{id}/accept` · `/decline` | Worker | Accept / decline |
| `GET` | `/api/company/members` | Owner | List workers (with gifted counts) |
| `DELETE` | `/api/company/members/{id}` | Owner | Fire a worker → block their gifted vouchers |
| `POST` | `/api/company/vouchers/gift` | Owner | Gift company vouchers to a worker |
| `POST` | `/api/company/vouchers/recall/{voucherId}` | Owner | Recall a gifted voucher |
| `GET` `POST` | `/api/legal-entity/profile` | ✅ | Read / upsert the caller's company profile |

### Public catalog

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/stations` · `/api/station-nodes` | Stations and physical locations |
| `GET` | `/api/stations/fuel-types` | Fuel types with base/discount pricing (public catalog) |
| `GET` | `/api/packages` · `/api/packages/station/{id}` | Fuel packages (all / by station) |
| `GET` | `/api/report` | Per-user report (authenticated) |

### Admin

Admin CRUD/reporting lives under `/api/admin/*` (all `Admin`-role): `stations`, `fuel-types`,
`packages`, `providers`, `vouchers`, `fuel-vouchers`, `voucher-imports`, `qr-codes`, `orders`
(incl. `POST /api/admin/orders/{id}/refund`), `users`, `settings`, `report`, `audit`, `errors`,
`legal-entity/contracts`, and `dashboard` (aggregated counts). See
[docs/RECONCILIATION.md](docs/RECONCILIATION.md) for the reconciliation-specific endpoints.

### Monitoring

| Path | Description |
|---|---|
| `GET /health` | Health check — probes the database, returns `{"status":"healthy","database":"connected"}` or 503 |
| `GET /swagger` | OpenAPI UI (development only) |
| `GET /hangfire` | Hangfire dashboard (Admin auth, or dev bypass) |

---

## Local development

### Prerequisites

- .NET 10 SDK
- Node.js (admin + mobile)
- PostgreSQL (or the Docker Compose stack); Redis for caching

### Full stack via Docker Compose

```bash
docker-compose up --build
```

### Backend only

```bash
cd backend/src/FuelFlow.API
dotnet run
```

The API listens on `http://localhost:5202` by default (see `Properties/launchSettings.json`).
The Hangfire dashboard is at `/hangfire`, Swagger at `/swagger`.

### Admin frontend

```bash
cd admin
npm install
npm run dev        # Vite dev server on :5173, proxies /api → localhost:5202
```

### Mobile app

```bash
cd mobile
npm install
echo "EXPO_PUBLIC_API_URL=http://localhost:5202" > .env
npm start -- --ios       # or --android
```

> **Physical device:** `localhost` is not reachable from a physical device. Use the machine's
> LAN IP (e.g. `http://192.168.x.x:5202`) or the production URL.

### Database migrations

Migrations **auto-apply on startup** by default (`RunMigrationsOnBoot`, code default `true`).
Set `RunMigrationsOnBoot=false` to manage them manually:

```bash
cd backend/src/FuelFlow.API
dotnet ef database update
```

A step-by-step API walkthrough (admin + user flows, with Postman) lives in
[docs/MANUAL_TESTING.md](docs/MANUAL_TESTING.md).

---

## Environment variables

Backend (`appsettings.json` / environment overrides — use `__` for nested keys in env vars):

```json
{
  "Database":  { "ConnectionString": "Host=...;Port=5432;Database=fuelflow;Username=postgres;Password=..." },
  "Jwt":       { "Secret": "<random, >=32 chars in production>" },
  "Auth":      { "DevBypass": false },
  "Monobank":  { "Enabled": true, "Token": "...", "WebhookUrl": "...", "PublicKey": "..." },
  "RunMigrationsOnBoot": true
}
```

- **`Jwt:Secret`** — required (≥32 chars) in non-development environments; the app fails fast otherwise.
- **`Auth:DevBypass`** — `true` enables the `000000` OTP + fake SMS; keep `false` in production.
- **`Monobank:PublicKey`** — PEM (or base64-of-PEM) used to verify webhook signatures; production fails fast if `Monobank:Enabled=true` with a placeholder key.

Mobile (`mobile/.env`): `EXPO_PUBLIC_API_URL=...` · Admin (build arg in `admin/Dockerfile` / dev env): `VITE_API_URL=...`

Production values for all of the above live in `deploy/.env` on the server (see
[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)).

---

## Deployment

- **Everything** (API, admin, website, Postgres, Redis) runs on a single Hetzner server
  via Docker Compose (`deploy/docker-compose.prod.yml`), Caddy for TLS at the edge.
  Deploys are **automatic**: CI deploys every green merge to `main` (self-hosted runner,
  build on server + smoke tests). Full runbook:
  [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).
- **Mobile** — Expo / EAS (development builds + TestFlight); web export available.

Set sensitive values (`Database__ConnectionString`, `Jwt__Secret`, `Monobank__Token`,
`Monobank__WebhookUrl`, `Monobank__PublicKey`) in `deploy/.env` on the server — never committed.

---

## Documentation index

| Doc | Contents |
|---|---|
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Deploy & ops runbook: Hetzner stack, CI auto-deploy, `deploy/.env` variables, backups, troubleshooting |
| [docs/SECURITY.md](docs/SECURITY.md) | Auth & device-binding model (plain-language + the real implemented controls) + fraud-analysis findings |
| [docs/SECURITY_AUDIT_2026-08-21.md](docs/SECURITY_AUDIT_2026-08-21.md) | Pre-production security audit — open items only: verdict (NO GO), open findings, priorities, deploy checklist, watchlist, refuted-hypotheses appendix |
| [docs/DESIGN.md](docs/DESIGN.md) | Mobile design system (tokens, components, rules) + app structure & core flows |
| [docs/RECONCILIATION.md](docs/RECONCILIATION.md) | Admin & customer reconciliation, refunds, SQL queries |
| [docs/COMPANY_WORKERS.md](docs/COMPANY_WORKERS.md) | Company owner/worker feature (data model + `/api/company` API) |
| [docs/MANUAL_TESTING.md](docs/MANUAL_TESTING.md) | Step-by-step manual API test flows (with the Postman collection) |
| [docs/OBSERVABILITY.md](docs/OBSERVABILITY.md) | Prometheus/Grafana/Loki observability stack + Telegram alerting |

---

## Known limitations

| Area | Issue |
|---|---|
| **OTP dev bypass** | With `Auth:DevBypass=true`, code `000000` works for any phone and SMS is faked. Keep it `false` in production. |
| **Synchronous PDF import** | Import runs inside the request; very large PDFs can approach the client timeout. Bounded by an import-concurrency guard. |
| **Self-reported redemption** | `mark-used` is honor-system — there is no POS/pump integration proving fuel was dispensed. |

See [docs/SECURITY.md](docs/SECURITY.md) for the fraud-analysis findings and the
open/closed work packages.
