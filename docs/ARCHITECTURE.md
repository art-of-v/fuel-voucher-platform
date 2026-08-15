# FuelFlow — Overview

FuelFlow is a fuel-voucher platform: users buy fuel (in liters) at partner stations (OKKO, WOG, KLO) via the mobile app, pay with Monobank, and receive digital vouchers they redeem at the pump. The system imports, verifies, and manages physical voucher stock (scanned from PDF catalogs) and reconciles purchases against fuel sales.

Three applications share one backend:

| App | Tech | Purpose |
|---|---|---|
| `mobile/` | React Native + Expo (Expo Router, NativeWind, Zustand, TanStack Query) | End-user app: map, stations, packages, basket, checkout, vouchers, payments, profile |
| `admin/` | React 19 + Vite + TypeScript (shadcn/ui, Tailwind 4, TanStack Query, Zustand, Recharts) | Back-office: import vouchers, manage providers/users/contracts, reports, audit & error logs |
| `backend/` | .NET 10 (ASP.NET Core minimal API + controllers), EF Core 10, PostgreSQL | REST API, background jobs, Monobank integration, auth, outbox |

---

## Technology Stack

### Backend (`backend/`)
- **Runtime/Framework:** .NET 10, ASP.NET Core, C# — controllers with a light CQRS-style handler-per-feature layout (`Features/<Area>/<Operation>/<Name>Command|QueryHandler.cs`).
- **ORM:** Entity Framework Core 10 with PostgreSQL (Npgsql); schema maintained by EF migrations that auto-apply on startup.
- **Jobs:** Hangfire (PostgreSQL storage) for recurring jobs (order fulfillment, notifications) + an outbox pattern for reliable event delivery.
- **Caching:** Redis (`Microsoft.Extensions.Caching.StackExchangeRedis`).
- **Payments:** Monobank invoice API (with a stub client for local dev) + signed webhooks.
- **SMS / auth:** Twilio SMS (stub in dev), JWT access (15 min) + refresh (7 days) tokens, device/challenge auth with HMAC signatures for protected endpoints.
- **PDF/QR:** Skia-based `PdfRenderer` (200 DPI) for rasterizing voucher catalogs, a custom QR decoder for payload extraction, and per-brand parsers (`Okko`, `Wog`, `Klo`) + fuzzy verification (confidence/mismatch %).
- **Logging:** Serilog (console) + a `DatabaseLoggerProvider` that asynchronously persists `Error`/`Critical` records to an `error_logs` table; a `RequestLoggingMiddleware` logs every request (method, path, origin, status, duration, IP).

### Admin panel (`admin/`)
- React 19, TypeScript, Vite 7.
- shadcn/ui components (Radix primitives), Tailwind CSS 4, lucide icons, sonner toasts.
- TanStack Query for server state; Zustand (persisted) for i18n language + client auth.
- i18n: EN / UK / DE / ES via a custom lightweight store in `src/lib/i18n.ts`.
- Consumes the API through `src/lib/api-client.ts` (JWT refresh, retries, timeouts).

### Mobile app (`mobile/`)
- React Native 0.81, Expo 54, Expo Router (file-based routing).
- NativeWind (Tailwind) styling, Zustand stores, TanStack Query.
- `react-native-maps` for the station map, `qrcode`/SVG for voucher QR codes, `expo-secure-store` + `react-native-biometrics` for tokens/biometric login, `expo-local-authentication` for device PIN.
- Shipable to iOS/Android and web (Expo web export).

### Infrastructure
- **Render** — Dockerized backend web service (auto-deploy from GitHub) + PostgreSQL + Redis.
- **Vercel** — hosted admin panel.
- **GitHub** — source, PRs (`fix/ef-notracking-detached-updates` branch), auto-deploy.

---

## Architecture

### Backend layout
```
backend/src/FuelFlow.API/
├── Program.cs                      # Bootstrap, Serilog, DI, Hangfire setup
├── Extensions/                     # ServiceSetup, PipelineSetup (middleware order)
├── Middleware/                     # RequestLogging, GlobalExceptionHandler, Session/Device validation
├── Persistence/                    # ApplicationDbContext, SeedData
├── SharedKernel/                   # Domain entities (Order, FuelVoucher, FuelPackage…)
└── Features/
    ├── Auth/                       # SMS code login, JWT refresh, device registration, admin users
    ├── Orders/                     # Checkout (bulk), Monobank invoices, fulfillment, outbox events
    ├── Vouchers/                   # Import (PDF/QR), catalog, inventory, user vouchers, verification
    ├── Providers/ & Stations/      # Fuel packages, per-liter pricing (supplier/margin/final)
    ├── Admin/                      # Dashboard, reconciliation, admin vouchers/users/orders
    ├── Report/                     # Per-user profit/spending reports
    ├── Contracts/                  # Client contracts & digital signatures
    ├── Audit/                      # Event-sourced audit log (audit_log table)
    ├── ErrorLogs/                  # error_logs table + admin UI
    ├── Monobank/                   # Webhook endpoint (signed)
    ├── Notifications/              # Order-fulfilled event processing
    ├── Referral/ & Sync/           # Referrals, station catalog sync
    └── ...
```

**Patterns:**
- **CQRS-lite:** each endpoint maps to a `*Command`/`*Query` + `*Handler`; handlers are `AsNoTracking()` by default (read paths) with explicit tracked writes.
- **Outbox:** domain events (e.g. `OrderCreated`, `OrderFulfilled`) are written to `outbox_events` in the same transaction; a recurring job (or the `FuelFlow.JobsWorker`) dispatches them to consumers (fulfillment, notifications).
- **API-first:** the same backend serves the mobile app, the admin panel, and Monobank webhooks.

### Request pipeline (backend)
```
RequestLoggingMiddleware → ExceptionHandler → CORS → ResponseCaching → RateLimiter
  → Authentication → SessionValidation → Authorization → DeviceSignature → Controllers
```

---

## Key Flows

### 1. Authentication
1. User enters phone in the mobile app → `POST /api/auth/send-code` → SMS (Twilio) with a 6-digit code.
2. `POST /api/auth/verify-code` returns JWT **access** (15 min) + **refresh** (7 days) tokens.
3. Devices register (`POST /api/auth/devices`) — challenge/signature auth protects checkout; biometric/PIN unlock is local.
4. Access tokens are refreshed transparently by both clients (`handle401` flow); admin uses `POST /api/admin/auth/*`.

### 2. Voucher import (admin)
1. Admin uploads a PDF catalog (`POST /api/voucher-catalog/import` — multipart, 300 s client timeout).
2. Backend rasterizes pages via `PdfRenderer` (200 DPI), reads each QR payload, and parses it with the brand-specific parser (OKKO/WOG/KLO).
3. Each voucher is **verified** (QR payload ↔ printed data, confidence/mismatch %) and deduplicated by voucher number / payload hash sets.
4. Valid vouchers are stored as `FuelVoucher` records (status `Imported`); results (`imported`/`failed`/`duplicates`) are returned and shown in the admin Import tab.
5. Batch history is tracked (`GetImportBatches`) so admin can review every import.

### 3. Purchase / checkout (mobile)
1. User picks a station on the map → selects fuel packages (liters × qty) → basket.
2. `POST /api/orders/checkout` (HMAC-signed) → backend creates an order (`PendingPayment`) and a Monobank invoice, returns a payment URL.
3. User pays in Monobank → Monobank calls the signed webhook → order becomes `Paid` and an `OrderCreated` outbox event is queued.
4. Fulfillment (Hangfire `*/1 * * * *`): for each paid order line, an `Available` voucher matching `(provider, fuelTypeId, liters)` is reserved and assigned to the user; the order moves to `Fulfilled` (or `PartiallyFulfilled` if stock is short).
5. User sees vouchers in-app, and redemption at the pump marks them `Used`.

### 4. Reconciliation & reports (admin)
1. Reconciliation compares paid orders vs. redeemed vouchers vs. imported stock for a period.
2. User Reports are a ledger: received (payments) — delivered value (fulfilled vouchers) — refunded. **Profit** is earned margin on liters actually delivered via vouchers (`FuelPackage.MarginUahPerLiter × liters × delivered quantity`).
3. Exportable as PDF act / CSV; dashboard shows revenue/profit aggregates.

### 5. Observability & audit
- Every admin/domain change is written to the audit log; every `Error`/`Critical` is persisted to `error_logs` and shown in the admin **Error Logs** tab.
- `RequestLoggingMiddleware` logs all traffic for diagnosing issues like aborted imports.

---

## Money and Currency Units

**Convention: the domain model stores money in whole-UAH integers; kopecks exist
only at the Monobank boundary.**

| Unit | Where |
|---|---|
| UAH (int) | `orders.price`, `order_line_items.unit_price` / `line_total`, `fuel_packages.price` / `original_price`, `fuel_types.base_price` / `discount_price` (per liter) |
| kopecks (int) | `refunds.amount`; every Monobank API call (`invoice/create`, `invoice/cancel`, webhooks) and DTO fields explicitly named `...Kopecks` |

Rationale: Monobank's merchant API mandates kopeck integers, and business prices
are whole UAH, so keeping the domain in UAH avoids floating-point money math
without paying kopeck-conversion cost everywhere.

Rules:

1. Every UAH↔kopeck conversion goes through `FuelFlow.SharedKernel.Money`
   (`ToKopecks` / `FromKopecks`). A bare `* 100` / `/ 100` in code review is a
   unit-mismatch suspect — that exact mistake shipped kopecks into
   `fuel_types.base_price` and rendered “8492.00 ₴/L” in the mobile app.
2. Frontends receive kopecks only from fields named `...Kopecks` and divide by
   100 at display time; everything else is UAH.
3. If fractional UAH pricing is ever required, migrate the domain columns to
   kopecks deliberately (data migration + all display sites) rather than mixing
   units again.

---

## Deployment

- **Backend:** Render web service (`render.yaml`, Docker, auto-deploy on push to the tracked branch) → `https://fuel-voucher-platform.onrender.com`.
- **Admin:** Vercel (`https://fuel-flow-opal.vercel.app`); CORS allows that origin.
- **Mobile:** Expo (development builds); web export available.
- Migrations apply automatically on backend startup.
