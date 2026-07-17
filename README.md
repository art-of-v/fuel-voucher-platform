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
10. [Security](#security)
11. [Operational Concerns](#operational-concerns)
12. [Known Limitations & Risks](#known-limitations--risks)

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
│                 Backend API (.NET)                            │
│  ASP.NET Core 10 · EF Core · Npgsql · Hangfire (in-process)  │
│  Clean Architecture (Controllers → Services → Repositories)  │
├──────────────┬───────────────────────┬───────────────────────┤
│  PostgreSQL  │  Hangfire (embedded)  │  External APIs        │
│  (Supabase)  │  job queue + scheduler│  Monobank / Twilio    │
└──────────────┴───────────────────────┴───────────────────────┘
                         │ (Admin manages via)
┌────────────────────────▼─────────────────────────────────────┐
│               Admin Frontend (React SPA)                     │
│  Vite · Tailwind CSS v4 · TanStack Query                     │
│  Voucher import, station/package management, QR viewer       │
└──────────────────────────────────────────────────────────────┘
```

**Key design decisions:**
- The backend is the single source of truth. Both the mobile app and the admin frontend are pure API clients.
- Orders are decoupled from voucher availability. A purchase can succeed even with zero inventory; when vouchers are imported later, the fulfillment job automatically backfills pending orders.
- Background jobs (fulfillment, notifications) run in-process via Hangfire, no separate worker deployment needed.
- JWT bearer tokens carry authentication state. Admin endpoints require the `Admin` role claim.

---

## Repository Structure

```
FuelFlow/
├── admin/                    # React Admin Dashboard (Vite + Tailwind v4)
├── backend/                  # .NET ASP.NET Core API
│   └── src/
│       ├── FuelFlow.API/     # Web API project (controllers, services, EF, Hangfire jobs)
│       └── FuelFlow.JobsWorker/  # Standalone Hangfire worker (optional; jobs run in API now)
├── mobile/                   # Expo React Native app
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
├── docs/                      # Documentation (deploy, security, testing)
├── docker-compose.yml         # Full local stack
├── render.yaml                # Render.com deployment config
└── .env.example               # Template for required environment variables
```

---

## Component Breakdown

### Admin Backend

**Project:** `backend/src/FuelFlow.API/`

The backend is an ASP.NET Core 9 Web API with EF Core on PostgreSQL. It follows Clean Architecture with controllers, application services, and the EF `DbContext` as the data layer.

**Key projects:**

| Project | Responsibility |
|---|---|---|
| `FuelFlow.API` | Web API — controllers, commands/queries, EF migrations, DI setup, Hangfire jobs |
| `FuelFlow.JobsWorker` | Standalone Hangfire worker (optional — jobs run in-process in the API by default) |

**Key services:**

| Service | File | Responsibility |
|---|---|---|
| `AuthController` | `Features/Auth/AuthController.cs` | OTP send/verify via phone, JWT issuance |
| `VoucherImportService` | `Features/Vouchers/Import/` | PDF → images → QR decode → voucher DB insert |
| `FulfillmentService` | `BackgroundJobs/FulfillmentService.cs` | Async voucher-to-order assignment (Hangfire recurring job) |
| `NotificationService` | `BackgroundJobs/NotificationService.cs` | User notifications on order fulfillment (Hangfire recurring job) |
| `QrGeneratorV2` | `Features/Vouchers/Import/Services/QrGeneratorV2.cs` | QR PNG rendering from DB-stored parameters |

**Architecture layers:**

```
HTTP Request
    └─▶ Controller (Features/{Feature}/Controller.cs)
            └─▶ Service / Command Handler
                    └─▶ DbContext / EF Core
                            └─▶ PostgreSQL
```

**Authentication flow:**

1. `POST /api/auth/send-code` — sends 6-digit code to phone (random in production, `000000` in dev).
2. `POST /api/auth/verify` — validates code, returns JWT bearer token.
3. Admin endpoints require `[Authorize(Roles = "Admin")]` claim in the JWT.
4. Mobile endpoints use JWT via `x-auth-token` header (or cookie fallback).

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

The frontend proxies all `/api` requests to `http://localhost:5202` during development via Vite's dev server.

---

### Mobile App (Expo React Native)

Built with Expo SDK and Expo Router for file-based routing.

**Authentication state management:**

JWT tokens are stored in `expo-secure-store` and attached as `x-auth-token` header to API requests. Two sources of truth are synchronized automatically:
- **`useAuth` hook** — queries `GET /api/auth/user/me` (server authoritative via JWT)
- **Zustand store** — `isAuthenticated` boolean (local, optimistic)

`AuthSync` component in `_layout.tsx` reconciles these: if the server returns 401 but the local store thinks authenticated, it forces logout and redirects to `/landing`. App lock gate uses biometric (Face ID) verification before granting access.

**Purchase flow:**

1. User browses packages (`/packages`) and adds items to cart (Zustand store).
2. Cart review at `/basket`, then proceeds to `/checkout`.
3. User pays via Monobank invoice. `POST /api/purchases` creates an order (`PendingPayment`).
4. Monobank sends a webhook to `POST /api/monobank/webhook` on success → order moves to `PendingFulfillment` + `OrderCreated` outbox event is published.
5. The Hangfire `FulfillmentService` (runs every 1 min) picks up the event and assigns available vouchers to the order.
6. `NotificationService` creates an in-app notification when fulfillment completes.
7. On success: cart cleared, redirected to `/my-codes`.

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
POST /api/purchases          →  creates order (status=PendingPayment)
                                creates Monobank invoice
        │
Monobank webhook (success)   →  order → PendingFulfillment
                                + ORDER_CREATED outbox event
        │                     (or dev: POST /api/purchases/simulate)
        ▼
Hangfire recurring job (every 1 min)
  └── FulfillmentService.ProcessPendingOrdersAsync()
        │
        ├── reads unprocessed ORDER_CREATED outbox events
        │   OR backfills orders with PendingFulfillment/PartiallyFulfilled
        │
        ▼
AssignVouchersToOrder()
        ├── SELECT ... FROM vouchers WHERE status='available'
        │     AND provider/fuelType/liters match
        ├── ORDER BY expirationDate ASC  (FEFO — First Expiry, First Out)
        ├── UPDATE vouchers SET assignedToUserId, status='Assigned'
        └── INSERT INTO fulfillments (orderId, voucherId)
        │
    If all vouchers assigned:
        ├── UPDATE orders SET status='Fulfilled'
        └── ORDER_FULFILLED outbox event
    If partial:
        └── UPDATE orders SET status='PartiallyFulfilled'
        │
        ▼
Hangfire recurring job (every 1 min)
  └── NotificationService.ProcessOrderFulfilledEventsAsync()
        └── Creates Notification record for user ("Order completed")
        │
Mobile app polls /api/sync/orders + /api/vouchers/my  →  user sees voucher
```

### Voucher Import

```
Admin uploads PDF
        │
POST /api/vouchers/import  (multipart, field name: file)
        │
For each page in PDF:
  1. Render page to image (ImageSharp)
  2. Detect voucher regions
  3. For each region:
     a. Decode QR code (ZXing) → extract payload + QR parameters
     b. Parse text (OCR words from PDF) → extract liters, date, number
     c. Resolve fuel type from text (longest match) or QR product code
     d. Create FuelVoucher record with QrParameters FK
        │
FulfillmentService (Hangfire recurring job, runs every 1 min)
  └── Processes ORDER_CREATED outbox events + backfills open orders
      └── Assigns vouchers via FEFO (nearest expiry first)
```

---

## Database Schema

All tables live in a single PostgreSQL database. The schema is managed via EF Core migrations in `backend/src/FuelFlow.API/Migrations/`.

| Table | Purpose |
|---|---|
| `users` | User accounts; keyed by UUID; stores phone (unique), email |
| `phone_verifications` | OTP records |
| `stations` | Fuel station brands (OKKO, WOG, etc.) |
| `station_nodes` | Individual physical station locations with lat/lng |
| `fuel_types` | Fuel type definitions per station with base and discount pricing |
| `fuel_packages` | Saleable packages (station + fuel type + liters + price) |
| `fuel_vouchers` | Voucher inventory; `qrImage` rendered server-side from `qr_parameters` |
| `qr_parameters` | QR encoding config (version, ECC level, mask pattern, encoding mode) |
| `orders` | Purchase orders; linked to vouchers via fulfillments |
| `fulfillments` | Junction table linking orders to vouchers |
| `voucher_imports` | Tracks batch PDF import jobs |
| `voucher_import_errors` | Per-voucher import failure reasons |

---

## API Reference

### Auth

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/send-code` | Send OTP to phone number |
| `POST` | `/api/auth/verify` | Verify OTP, return JWT |
| `GET` | `/api/auth/user/me` | Get current authenticated user |
| `POST` | `/api/auth/device/logout` | Revoke device session |

### Purchases & Orders

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/purchases` | ✅ | Create purchase + Monobank invoice |
| `GET` | `/api/purchases/my` | ✅ | Get current user's purchases |
| `GET` | `/api/sync/orders` | ✅ | Get user's orders |
| `POST` | `/api/monobank/webhook` | — | Monobank payment callback (triggers fulfillment) |

### Monitoring

| Method | Path | Description |
|---|---|---|
| `GET` | `/hangfire` | Hangfire dashboard — job history, retries, scheduling |
| `GET` | `/health` | Health check |

### Vouchers (User)

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/vouchers/my` | ✅ | Get user's assigned vouchers (includes `qrImage`) |
| `PATCH` | `/api/vouchers/:id/mark-used` | ✅ | Mark a voucher as used |
| `PATCH` | `/api/vouchers/:id/restore` | ✅ | Restore a used voucher |

### Public

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/stations` | List all stations |
| `GET` | `/api/station-nodes` | List all station locations |
| `GET` | `/api/packages` | List all fuel packages |
| `GET` | `/api/inventory` | Aggregated voucher inventory |

### Admin (requires JWT with Admin role)

| Method | Path | Description |
|---|---|---|
| `GET/POST/PUT/DELETE` | `/api/admin/stations` | Station management |
| `GET/POST/PUT/DELETE` | `/api/admin/fuel-types` | Fuel type management |
| `GET/POST/PUT/DELETE` | `/api/admin/packages` | Package management |
| `GET` | `/api/admin/vouchers` | Paginated voucher list with QR images |
| `GET` | `/api/admin/vouchers/{id}` | Single voucher detail with QR |
| `POST` | `/api/vouchers/import` | Upload PDF (multipart, field: `file`) |
| `GET` | `/api/vouchers/{id}/qr` | Render QR image for a voucher |

---

## Local Development Setup

### Prerequisites

- .NET 10 SDK
- Docker & Docker Compose
- A PostgreSQL database (or use the Docker Compose stack)

### Full Stack via Docker Compose

```bash
docker-compose up --build
```

Services available:
- **Backend API:** http://localhost:5001 (or 5202 with Kestrel)
- **Admin Frontend:** http://localhost:5173
- **Mobile App (web preview):** http://localhost:5003

### Backend Only (local dev)

```bash
cd backend/src/FuelFlow.API
dotnet run
```

The API is available at `http://localhost:5202` by default (see `Properties/launchSettings.json`).
The **Hangfire dashboard** is available at `/hangfire` for monitoring background jobs.

### Admin Frontend Only

```bash
cd admin
npm install
npm run dev                # Vite dev server on :5173
                           # Proxies /api → localhost:5202
```

### Mobile App

```bash
cd mobile
npm install

# Create .env with the API URL
echo "EXPO_PUBLIC_API_URL=http://localhost:5202" > .env

# Run on iOS simulator
npm start -- --ios

# Run on Android
npm start -- --android
```

> **Physical Device Note:** The device must be able to reach the backend URL. Local `localhost` is not accessible from a physical device. Use the production URL or the machine's LAN IP (e.g., `http://192.168.x.x:5202`).

### Database Migrations

The schema is managed via EF Core migrations:

```bash
cd backend/src/FuelFlow.API
dotnet ef database update
```

---

## Production Deployment (Render)

The .NET backend is deployed to [Render](https://render.com) via `render.yaml`.

**Service:** `fuel-dotnet-backend`
- **Build:** `dotnet restore && dotnet publish`
- **Start:** `dotnet FuelFlow.API.dll`
- **Root directory:** `backend/src/FuelFlow.API`

**Required secrets to set in Render Dashboard (not in render.yaml):**

| Key | Description |
|---|---|
| `Database__ConnectionString` | Supabase PostgreSQL connection string (URL-encoded) |
| `Monobank__Token` | Monobank merchant API token |
| `Monobank__WebhookUrl` | Webhook URL for Monobank callbacks |
| `Monobank__PublicKey` | Monobank's PEM public key for webhook verification |
| `RunMigrationsOnBoot` | `true` to apply EF migrations on startup |

**Current live backend:** `https://fuel-voucher-platform.onrender.com`

---

## Environment Variables

Complete reference for `backend/src/FuelFlow.API/appsettings.json` (or environment overrides):

```json
{
  "Database": {
    "ConnectionString": "Host=...;Port=5432;Database=fuelflow;Username=postgres;Password=..."
  },
  "Monobank": {
    "Token": "...",
    "WebhookUrl": "...",
    "PublicKey": "..."
  },
  "RunMigrationsOnBoot": false
}
```

Mobile app (`mobile/.env`):

```env
EXPO_PUBLIC_API_URL=https://fuel-voucher-platform.onrender.com
```

---

## Operational Concerns

### Logging

The .NET backend uses structured logging via `ILogger<T>`. In development, logs appear in the console. On Render, logs are available in the Render dashboard.

### Authentication

- Phone-based OTP auth returns a JWT bearer token (1h expiry).
- Admin endpoints require `[Authorize(Roles = "Admin")]`.
- Login flow: phone → `POST /api/auth/send-code` → code → `POST /api/auth/verify` → JWT.
- Phone numbers are normalized to E.164 format (`+380XXXXXXXXX`).

### Background Jobs

Hangfire runs **in-process** inside the API project — no separate worker deployment is needed. Two recurring jobs execute every minute:

| Job | Service | Description |
|---|---|---|
| `process-fulfillments` | `FulfillmentService` | Processes `ORDER_CREATED` outbox events + backfills open orders |
| `process-notifications` | `NotificationService` | Creates user notifications after order fulfillment |

The Hangfire dashboard is available at `/hangfire`. Job history, retries, and scheduling can be monitored there.

### Scaling

- The API is stateless and can be horizontally scaled.
- Hangfire uses PostgreSQL for job storage and supports multiple instances if the `FuelFlow.JobsWorker` is deployed separately.

---

## Security

See [docs/SECURITY.md](./docs/SECURITY.md) for:
- Simple explanation of the cryptographic authentication model
- Technical architecture for device binding, challenge-response, and biometric-gated keys
- Database schema for devices, sessions, and OTP codes
- App integrity protections (SSL pinning, attestation, rate limiting)

---

## Known Limitations & Risks

| Area | Issue |
|---|---|
| **OTP dev bypass** | In development mode, code `000000` works for any phone. Production uses random codes logged to Render logs. |
| **No WOG voucher imports tested** | Only OKKO voucher PDFs have been tested through the import pipeline. WOG QR rendering is untested. |
| **Import is synchronous** | PDF import runs synchronously in the request. Large PDFs may timeout. |
| **Expiration check commented out** | `v.ExpirationDate > DateOnly.FromDateTime(DateTime.UtcNow)` is temporarily disabled in both `FulfillmentService.cs` files to allow testing. **TODO:** re-enable before prod. |
| ~~**Monobank webhook didn't trigger fulfillment**~~ | ~~Real payments set orders to `Paid` but never created the outbox event.~~ **Fixed:** webhook handler now transitions to `PendingFulfillment` + publishes `ORDER_CREATED` outbox event. |
