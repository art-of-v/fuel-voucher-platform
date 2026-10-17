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
4. [Who holds a voucher's QR](#who-holds-a-vouchers-qr)
5. [Money & currency](#money--currency)
6. [Database schema](#database-schema)
7. [API reference](#api-reference)
8. [Local development](#local-development)
9. [Environment variables](#environment-variables)
10. [Deployment](#deployment)
11. [Documentation index](#documentation-index)
12. [Known limitations](#known-limitations)

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
- Orders are **gated** on voucher availability. A package is sellable only while at least one `Available` voucher matches its `(provider, fuelTypeId, liters)`; `GET /api/packages` reports this per package as `isAvailable`, and checkout refuses a line with no stock (409 `package_out_of_stock`). An unavailable package is still **listed** — the app greys it out with the reason rather than hiding it. Stock that runs out *after* the sale is still the worker's problem: fulfilment backfills `PartiallyFulfilled` orders FEFO once stock arrives (First Expiry, First Out), which also covers orders that predate this gate.
- A voucher in somebody's hands always belongs to the order that delivered it — `fuel_vouchers.order_id` is DB-enforced for every held voucher, and an order that still owns one cannot be deleted. A **paid renewal is protected from the other end**: its order_line_items row is what refuses the deletion (order_id RESTRICT, not CASCADE), because that row is the customer's receipt for the term they bought. `orders.kind` separates fuel bought for money (`Purchase`), a customer's paid renewal (`Renewal`) and a company handing its own fuel to a worker (`ReceivedFromCompany`, priced 0 and excluded from every money view).
- Money-affecting values (checkout price, webhook amount, order status) are computed and enforced server-side; the client only ever *proposes*.
- Background jobs run **in-process** via Hangfire (PostgreSQL storage) by default; the same jobs can also run in the standalone `FuelFlow.JobsWorker` for horizontal scaling.
- Domain events use an **outbox**: events are written in the same transaction as the state change, then dispatched to consumers (fulfillment, notifications) by a recurring job.
- JWT bearer tokens carry identity; admin endpoints require the `Admin` role claim; a per-request middleware re-checks the session against the DB so revocation is immediate (see `docs/SECURITY.md` in the private docs repo).

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
│       ├── Feedback/              # Telegram feedback bot: webhook, triage, admin backlog
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
├── website/                  # Next.js marketing site behind palne.shop (static export)
├── deploy/                   # Hetzner production stack (compose, Caddyfile, backup/restore)
├── docs/                     # Documentation (see the Documentation index below)
├── scripts/                  # Repository maintenance helpers
├── .github/                  # CI workflows
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
   Access tokens live **15 min** in production (200 min in dev); refresh tokens live **14 days**.
   New phone numbers auto-create a user with `is_active = false` — activation is a separate
   staff action, and an inactive account may browse/renew but is refused at checkout. Deleting
   an account is a **soft** delete, so the phone is released (`users.phone_number` is unique only
   `WHERE is_deleted = false`) and verifying a new code mints a **fresh** row with no history
   carried over. The one exception is the QA phone, which re-registers already flagged
   `is_qa_account` and active — see the planning repo's `docs/QA_TEST_ACCESS.md`.
3. Clients refresh transparently via `POST /api/auth/refresh` (refresh-token rotation with
   family reuse detection). The admin panel restores a session on load only when
   `localStorage["fuelflow.session"]` is set — a non-secret hint, not a credential. Without it
   the app resolves to "signed out" **without any network call**, so an operator who is not
   signed in never spends from the refresh limiter's three-per-minute budget. See
   `admin/src/lib/session.ts`.
4. The mobile app registers a device keypair and answers a server **challenge** with a
   signature (`/api/auth/device/*`); biometric/PIN unlock is a local gate. The device
   signature protects money-moving endpoints (checkout).

Staff use the **same** login flow; a role is granted by setting the user's `role_id` in the DB.
The staff hierarchy is `ProductOwner(100) > Admin(50) > Manager(10) > QA(5) > User(0)`, with the
level stored in `roles.level` and the authority matrix enforced in
`Features/Auth/AdminUser/RoleHierarchy.cs`:

| role | can do |
|---|---|
| `ProductOwner` | everything; the singleton, never assignable or re-roled by anyone |
| `Admin` | manage `User` / `Manager` accounts; ban and delete them |
| `Manager` | activate/deactivate `User` accounts only |
| `QA` | activate/deactivate `User` accounts only — the tester role; no ban, delete, or role assignment |
| `User` | no staff rights at all |

### 2. Voucher import (admin)

1. Admin uploads a PDF catalog: `POST /api/voucher-catalog/import` (multipart, field `file`; ~300 s client timeout).
2. The backend rasterizes each page with the Skia-based `PdfRenderer` (200 DPI), decodes the
   QR with a custom decoder, and parses the payload with the brand-specific parser (OKKO / WOG / KLO).
3. Each voucher is **verified** (decoded QR ↔ printed data, with a confidence/mismatch %) and
   **deduplicated globally** by voucher number *or* QR payload across the whole table.
4. Results (`imported` / `duplicates` / `failed` / `verificationFailed`) are returned and shown
   in the admin **Import** tab; batch history is retained.

### 3. Purchase / checkout (mobile)

1. User picks a station → selects fuel packages (liters × quantity) → basket. A package with no stock is
   shown but not buyable (`isAvailable: false`).
2. `POST /api/purchases` (or `POST /api/purchases/bulk`), device-signed, creates an order
   (`PendingPayment`), a **`payments` row** and a Monobank invoice, returning a payment URL.
   **Price is recomputed server-side from the catalogue** (`ServerPricing` over the station's
   `SellablePackage` — a `FuelOffer` joined with one of its `FuelOfferVolumes`) — the client value is
   only a proposal.
3. User pays in Monobank → Monobank calls `POST /api/monobank/webhook`. The signature
   (`X-Sign`, ECDSA over the raw body) is **verified and fail-closed (401)** and the amount is
   checked; on success the order → `PendingFulfillment` and an `OrderCreated` outbox event is queued.
   (In dev, `POST /api/purchases/simulate` — Admin — stands in for the webhook.)
   **The delivery is written to `webhook_events` before any of that runs**, so the acquirer's own
   signed statement survives whether or not we could act on it, and a redelivery is refused by the
   unique index rather than re-processed. An **amount mismatch answers 200, not 4xx**: the difference
   is permanent, so asking Monobank to retry only produces a loop — the money really was taken, the
   order stays `PendingPayment`, and the evidence lands as a `Failed` delivery with the acquirer's
   amount beside ours on the `payments` row. A **404** is still returned for an invoice whose order
   has not committed yet, because that one is a race and retrying is correct.
4. The Hangfire `FulfillmentService` (`*/1 * * * *`) assigns `Available` vouchers matching
   `(provider, fuelTypeId, liters)` via **FEFO**; the order → `Fulfilled`, or `PartiallyFulfilled` when
   stock ran short between the checkout gate and the claim (or when the order predates that gate).
   `NotificationService` then creates an in-app notification.
   **Each fulfilment records the line it satisfied** (`fulfillments.line_item_id`), which is what lets
   the same job's reconciliation compare *per line* rather than per litre count. Litres cannot
   distinguish `OKKO A95 10 L ×2` from `WOG A95 10 L ×1`, so an order short on one line while
   over-delivered on the other compared equal and passed. Rows written before the column stay null and
   fall back to the litres comparison — see `FulfillmentService.HasPerLineMismatch`.
5. The app polls `GET /api/vouchers/my` + `GET /api/sync/orders`; redemption at the pump is
   self-reported via `PATCH /api/vouchers/{id}/mark-used`.

### 4. Feedback bot (mobile)

1. The bottom bar carries a **Feedback** tab, visible only when `GET /api/feedback/deep-link`
   answers — the tab is *absent*, not disabled, so switching `FeedbackBot:Enabled` off in the
   admin takes it out of every installed app without a release. The gate is re-checked on every
   foreground transition (throttled to once a minute), so flipping it either way takes effect
   without the user relaunching; a network failure leaves the previous answer alone rather than
   hiding the tab or flashing it.
2. That endpoint returns a **server-signed** `https://t.me/<bot>?start=<payload>` link. The app
   never signs anything: a key inside a shipped binary is a public key, and would let anyone start
   a chat as any account.
3. The customer writes in a **private 1-on-1 Telegram chat** — other customers cannot see it.
   Every message is acknowledged immediately, stored in `feedback_messages`, and queued for
   triage out of band (the webhook answers 200 first; a classifier round-trip in the request
   would make Telegram redeliver the same update forever).
4. `FeedbackTriageService` (Hangfire) asks an LLM (`Llm:*`, Gemini by default) whether the
   message carries information — greetings and jokes are dropped — and posts the rest, with a
   category, a severity and a one-sentence restatement, to a **private staff group**. With the
   classifier off (the default) nothing is filtered and every message is posted untriaged: the
   post then carries the author and the raw text, with no category/severity header, because there
   is nothing to say. Dropped messages are still stored, so a classifier that starts eating real
   complaints is visible in the admin's "unsure drops" filter instead of vanishing.
5. A staff member answers by **quoting the bot's post** in that group. Two independent gates
   relay it to the customer: the sender must be in `Telegram:Feedback:StaffUserIds`, and the
   quoted message must be one the bot posted. Every answer is recorded in
   `feedback_message_replies`.
6. Everything lands in the admin **Feedback** tab (filters, triage state, full history); Telegram
   is where the team works, the admin tab is the durable backlog.

Configured entirely off by default (`Telegram:Feedback:Enabled`, `Llm:Enabled`,
`app_settings.FeedbackBot:Enabled`). The last of those is the operational switch: the admin
**Settings** tab owns it alongside read-only `TelegramConfigured` (are the bot credentials
present at all?) and the live webhook state, and
`POST /api/admin/settings/feedback/webhook` re-registers the webhook against the address the
admin panel is being served on — so a redeploy or a domain change is a button rather than a
terminal. Design and rationale: `docs/FEEDBACK_BOT.md` in the private planning repo.

### 5. Reconciliation, refunds & reports (admin)

- Reconciliation compares paid orders vs. delivered vouchers vs. imported stock; the money
  ledger is `received = delivered value + refunded + outstanding`.
- Partial orders can be refunded (manual `POST /api/admin/orders/{id}/refund` or the optional
  auto-refund job) for the undelivered value via Monobank invoice cancellation; a `sync-refund-status`
  job reconciles refund state. See `docs/RECONCILIATION.md` in the private docs repo.
- **Profit** is earned margin on liters actually delivered (`FuelPackage.MarginUahPerLiter × liters × delivered qty`).

### 6. Company workers

A user who creates a `LegalEntity` becomes a company **owner**: they can buy vouchers for the
company (`legalEntityId` on checkout), invite registered users as **workers**, gift/recall
company vouchers, and fire workers (which blocks their gifted vouchers). Gifting records a
`ReceivedFromCompany` order that owns the fuel it hands over, so the worker sees a receipt; recall,
firing and the admin unblock return that fuel to the **purchase** it arrived under. See
`docs/COMPANY_WORKERS.md` in the private docs repo.

---

## Who holds a voucher's QR

A voucher's QR payload is the bearer instrument: whoever presents it at the pump spends the fuel.
So every route that hands it out answers the same question through one rule,
`VoucherHolder.CanRedeem` in
`backend/src/FuelFlow.API/Features/Vouchers/SharedModels/VoucherHolder.cs`:

> The holder is `WorkerUserId ?? AssignedToUserId`. A worker, once set, wins.

Issuing fuel to a worker sets `worker_user_id` and deliberately **leaves** `assigned_to_user_id` on
the owner, because the owner keeps the purchase lineage and the company's undistributed pool. The
two columns therefore name different people, and "assigned to" is not "held by":

| Caller | `GET /vouchers/my` | `GET /vouchers/{id}/qr` | `PATCH .../mark-used`, `.../restore` |
|---|---|---|---|
| The worker it was issued to | payload + PNG | PNG | allowed |
| The owner who issued it | row only, QR blanked | 403 | `Forbidden` |
| Anyone else | not in the list | 404 | `Forbidden` |

`GET /vouchers/my` still returns the **row** to the owner — the owner's hub reads that same list to
show what each order delivered and how much is still undistributed — but `qrPayload`, `qrCodeData`
and `imageUrl` are empty for a caller who is not the holder, so the mobile client renders its
existing "QR unavailable" placeholder. The payload itself is encrypted at rest
(`qr_payload` is `bytea`, AES-256-GCM); that stops a database dump from yielding codes, but it does
not help here, because the application decrypts it for whoever passes the check.

One rule, three call sites, because they once disagreed: `/vouchers/my` matched the owner on
`assigned_to_user_id` alone and returned the code and a rendered PNG for fuel they had just given
away, while `/vouchers/{id}/qr` refused them with 403. `VoucherQrScopeTests` covers the wallet and
`VoucherHolder` tests pin the rule itself.

---

## Money & currency

**Convention: the domain model stores money in decimal UAH to the kopeck; kopeck integers exist
only at the Monobank boundary.**

| Unit | Type | Where |
|---|---|---|
| UAH | `numeric(12,2)` | `orders.price`, `order_line_items.unit_price` / `line_total`, `fuel_offer_volumes.package_price`, `fuel_types.base_price` / `discount_price` |
| UAH per litre | `numeric(10,4)` | `fuel_vouchers.cost_per_liter`, `fuel_offers.*_per_liter`, `voucher_exchanges.cost_per_liter_applied` |
| UAH surcharge | `numeric(10,2)` | `voucher_exchanges.surcharge_uah` (**per-pair share**), `operator_voucher_renewals.surcharge_uah`; `movement_batches.surcharge_uah` is `numeric(14,2)` because it is the **whole** settlement, not one voucher's share of it |
| kopecks | `numeric(14,0)` | `monobank_statement_entries.amount_kopecks`, `payments.amount_kopecks`, `payments.provider_amount_kopecks`, `webhook_events.amount_kopecks`; every Monobank API call (`invoice/create`, `invoice/cancel`, webhooks) and any DTO field explicitly named `...Kopecks` |

Monobank's merchant API mandates kopeck integers, so every conversion goes through
`SharedKernel.Money` at that boundary and nowhere else. Per-litre rates keep 4 dp because
blended cost is a weighted average and rounding it to 2 dp drifts the cost of goods.

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
**The history is append-only**: a migration that any deployed database has applied stays in the
folder, in order, under its original id. It is never collapsed into a single baseline — see
[Database migrations](#database-migrations) for why, and for the gates that depend on it.
The primary tables:

| Table | Purpose |
|---|---|
| `users` | Accounts (UUID, email, role); `is_active` (activation gate — checkout only), `token_version`, `is_deleted` (soft delete) drive session revocation. `phone_number` is unique only `WHERE is_deleted = false`, so deleting an account releases the number for re-registration. `is_qa_account` marks the App-Review QA identity and gates the test-voucher catalogue and stock pool |
| `roles` | Role definitions with a level (`User` 0, `QA` 5, `Manager` 10, `Admin` 50, `ProductOwner` 100) |
| `verification_codes` | OTP records (stored hashed) |
| `devices` | Registered device public keys for challenge/signature auth |
| `refresh_tokens` | Rotating refresh tokens (`family_id` for reuse detection) |
| `push_tokens` | Per-device push registration |
| `stations` / `station_nodes` | Fuel brands and individual physical locations (lat/lng) |
| `fuel_types` | Fuel-type definitions per station with base/discount pricing (per liter, UAH). One **live** row per name per station (`ux_fuel_types_station_name`) - a second A-95 at the same brand used to be impossible because the id *was* the name (`okko-95`) and now is refused, because two rows for one fuel become two offers the storefront lists twice and a voucher importer can price against either. `brand_fuel_code` is the **brand's** own code for the grade (`A-95`, `DP`, `Pulls 95`), unique per brand (`ux_fuel_types_station_brand_fuel_code`) and **nullable**. Not the supplier's: a supplier is a third party we buy fuel *from*, and one brand's fuel comes from many of them. The brand publishes the code on the price sheet, so `ImportOkkoPumpPrices` matches on it by equality; a fuel without one is still matched by category, and a code the sheet does not carry means no price today rather than a guess from the name - which is the half of a fuel's identity that is not free text, and what a catalogue import will match on. Filled in by hand today, by the import when it lands |
| `fuel_offers` / `fuel_offer_volumes` | The saleable catalogue, split in two: an **offer** is (station × fuel type) and carries the commercial terms — supplier/margin/final price, pump price, min discount; a **volume** is one nominal on that offer (`10 L`, `20 L`, `50 L`) with its own package price. Price per litre therefore lives on the offer and is shared by its volumes; a nominal whose price per litre disagrees with the offer is drift, and `deploy/preflight-db` checks for it before a pull. An offer's **id is its fuel type id** (`-qa` for the rehearsal pool), not a concatenation: the offer's station is its fuel type's station, so spelling it out only repeated the brand (`okko-okko-95`). `ux_fuel_types_station_name` keeps one live fuel per name per station, and `011`/`012` ask before a pull what that key assumes |
| `suppliers` | Third parties fuel is bought from (deactivated, never hard-deleted) |
| `purchase_batches` | One row per (import × fuel): the batch's supplier and who entered it |
| `fuel_vouchers` | Voucher inventory; QR rendered from `qr_parameters`; carries `legal_entity_id` + `worker_user_id`; `order_id` names the order that delivered it and is **required** for a held voucher (`Assigned`/`Used`/`Blocked`, DB CHECK `ck_voucher_held_has_order`), null while it is stock |
| `qr_parameters` | QR encoding config (version, ECC level, mask, encoding mode). The unique index over the four is `NULLS NOT DISTINCT` — three of them are nullable, and a plain btree treats NULLs as distinct, so `('M', NULL, NULL, NULL)` could be inserted twice. CHECK-constraints pin `ecc_level` to `L/M/Q/H`, `version` to 1–40 and `mask_pattern` to 0–7 |
| `orders` / `order_line_items` | Purchase orders and their line items (UAH); `kind` = `Purchase` \| `Renewal` \| `ReceivedFromCompany` (a company→worker handover, priced 0, with `source_order_id` naming the purchase the fuel came from). A **renewal line states what it renews**: `term_code` (the term bought), `source_voucher_id` (RESTRICT onto the voucher — a renewal is an intent and exists from checkout, before any fulfilment) and `renewal_branch` (`extend`\|`replace`, stored rather than re-derived because the branch was decided at checkout from state that has since moved on). `ck_order_line_renewal_has_term` refuses a renewal line with no term, and `ck_order_line_renewal_pair_is_whole` refuses half a renewal. Once fulfilment applies the renewal it also writes the **expiry snapshots** here - `previous_customer_expiration` / `new_customer_expiration`, pinned as a pair by `ck_order_line_renewal_snapshots_are_paired` and ordered by `ck_order_line_renewal_expirations_ordered` - because the old expiry is overwritten in place on the voucher and exists nowhere else afterwards. **The line is now the whole renewal record**: `voucher_renewal_items` repeated all of it and was dropped (D-13, `20261017073518`), so what was bought and for how much is `unit_price`/`line_total` here, and what was *delivered* is the `Fulfillment` row whose `line_item_id` points here. `order_line_items.order_id` is **RESTRICT**, not CASCADE: this row is what keeps an order a customer paid a renewal on from being deleted |
| `payments` | **A payment as an entity, not four columns on `orders`.** One row per acquirer invoice, written when the invoice is created and moved by the webhook or the statement. `kind` = `Capture` \| `Refund`, so a refund is a payment and not a second book about the same money. Carries `paid_at_utc` (when the money moved), `status` (`Pending`/`Succeeded`/`Failed`/`Cancelled`/`Expired`), an `xmin` concurrency token, and **both** amounts: `amount_uah`/`amount_kopecks` are what we billed and `provider_amount_kopecks` is what the acquirer reported. `ck_payments_amount_units_agree` keeps the first two from drifting; the third is deliberately **not** constrained against them, because a mismatch is the finding. `ux_payments_one_refund_per_order` (`UNIQUE (order_id) WHERE kind = 'Refund'`) carries at most one refund per order; captures are unconstrained, because a re-issued payment link legitimately produces a second one. See `docs/TARGET_SCHEMA.md` D-10 |
| `webhook_events` | **Every acquirer callback, stored verbatim before anything acts on it.** `UNIQUE (provider, provider_event_id)` is the idempotency guarantee — a redelivery is refused by Postgres, not judged by a handler. `attempts > 1` means the acquirer retried us. `status` = `Received`/`Processed`/`Ignored`/`Failed`, and `Failed` is reachable through a partial index because it means "we were told and did nothing". Monobank sends no event id, so `provider_event_id` is derived from `(invoice, status, modifiedDate)` — see `WebhookEventIdentity`. See `docs/TARGET_SCHEMA.md` D-11 |
| `fulfillments` | Junction linking orders to the vouchers that satisfy them, and **which line** of the order each one satisfied (`line_item_id`, nullable — see below) |
| `refunds` | **Not a table.** A read-only VIEW over `payments` where `kind = 'Refund'`, kept only so saved queries and psql sessions against the old name keep working — nothing in the application reads it. A refund *is* a payment (`parent_payment_id` names the capture it reverses); the view joins `user_id` and `ext_ref` from `orders` rather than holding copies that could drift |
| `monobank_statement_entries` | Acquirer statement rows as imported — immutable evidence, never rewritten by a re-import |
| `voucher_imports` / `voucher_import_errors` | Batch PDF import jobs + per-voucher failures. `voucher_imports.status` is pinned to `Started`/`Completed`/`Failed` by CHECK and a `Started` row cannot carry a `completed_at_utc` — it was the last status column in the schema left as free text, on the table whose status gates the whole intake |
| `operator_voucher_renewals` | Operator-initiated renewals (extend vs replace, surcharge, invoice) |
| `voucher_exchanges` | One row per retired voucher in an operator↔provider stock exchange — the per-pair evidence. `old_voucher_id` is UNIQUE, which is the database half of the confirm idempotency; `exchange_batch_id` is a RESTRICT foreign key onto `movement_batches` since #319 |
| `movement_batches` | **The settlement itself** — an operator↔provider exchange as one agreed thing. `voucher_exchanges.exchange_batch_id` pointed at nothing from 2026-10-01 until #319, so the batch's lifecycle (who, when, how much, confirmed or rejected) had nowhere to live and every reader folded it back out of the member rows. Stated once here; the per-pair facts stay on the pairs |
| `voucher_movements` | **The one append-only ledger** every voucher's movements are recorded in (D-13). One row per movement: where the voucher came from, where it went, who, when, what it cost. Three decisions are load-bearing: `from_voucher_id` is **nullable** (a voucher arriving from a supplier PDF has no predecessor, and import creates the majority of vouchers here — `ck_voucher_movements_has_a_side` requires one side instead); `order_id` and both actor columns are **FK-free**, because an audit row must not be the thing that blocks deleting its own subject; and `ck_voucher_movements_distinct_sides` makes "extend" *unrepresentable* — pushing an expiry out moves no voucher, so it is a `voucher_events` state change, not a movement. Nothing writes it yet; `voucher_exchanges` and `operator_voucher_renewals` still stand |
| `legal_entities` | Company profiles (owner = the user who created one) |
| `company_invitations` / `company_members` | Owner↔worker invitations and membership |
| `contracts` / `user_contracts` | Client contracts and the signatures on them |
| `outbox_events` | Transactional event log dispatched by background jobs |
| `provider_event_outbox` | Immutable admin/domain change audit trail (before/after values, 365d) |
| `notifications` | In-app user notifications |
| `error_logs` | Persisted application errors (90d) |
| `support_messages` | Public support form submissions |
| `feedback_messages` / `feedback_message_replies` / `feedback_chat_bindings` | Customer feedback collected through the in-app Telegram bot (below) |
| `app_settings` | Runtime feature toggles an admin can flip without a deploy |

Referral codes are two columns on `users` (`referral_code`, `referred_by`), not a table of
their own. The admin Audit tab reads `provider_event_outbox`; there is no `audit_log` table.

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
| — | | | Both answer 409 with a stable code on a deliberate refusal: `below_cost`, `term_stock_unavailable`, `package_out_of_stock` |
| `GET` | `/api/purchases/my` | ✅ | Current user's purchases (with vouchers) |
| `POST` | `/api/purchases/simulate` | Admin | Simulate a Monobank callback (dev/testing) |
| `POST` | `/api/monobank/webhook` | — (signed) | Monobank payment callback — signature-verified, fail-closed 401 |
| `GET` | `/api/sync` · `/api/sync/orders` | ✅ | Combined sync (orders + totals) / user's orders |

### Vouchers

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/vouchers/my` | ✅ | User's vouchers (plain array; includes `source`, `legalEntityId`, `workerUserId`). `qrPayload`, `qrCodeData` and `imageUrl` are populated only for the voucher's current holder — see [Who holds a voucher's QR](#who-holds-a-vouchers-qr) |
| `PATCH` | `/api/vouchers/{id}/mark-used` | ✅ | Mark a voucher used (guarded to the voucher's current holder) |
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
| `DELETE` | `/api/company/members/{id}` | Owner | Fire a worker → block their gifted vouchers, returning them to the company's purchase |
| `POST` | `/api/company/vouchers/gift` | Owner | Gift company vouchers to a worker (creates one `ReceivedFromCompany` order) |
| `POST` | `/api/company/vouchers/recall/{voucherId}` | Owner | Recall a gifted voucher → back to the company pool and to its purchase |
| `POST` | `/api/company/vouchers/{block,unblock}/{voucherId}` | Owner | Freeze / thaw a worker's voucher (the voucher keeps its worker and its order) |
| `GET` `POST` | `/api/legal-entity/profile` | ✅ | Read / upsert the caller's company profile |

### Public catalog

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/stations` · `/api/station-nodes` | Stations and physical locations |
| `GET` | `/api/stations/fuel-types` | Fuel types with base/discount pricing (public catalog) |
| `GET` | `/api/packages` · `/api/packages/station/{id}` | Fuel packages (all / by station), each with `isAvailable` (scoped to the caller's own stock pool) |
| `GET` | `/api/report` | Per-user report (authenticated) |

### Feedback

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/feedback/deep-link` | ✅ | Server-signed `t.me` deep link for the signed-in user; **404 when the channel is off** (the mobile tab hides itself) |
| `POST` | `/api/telegram/feedback/webhook` | — (secret header) | Telegram webhook for the feedback bot; authenticates by `X-Telegram-Bot-Api-Secret-Token` (constant-time), always answers 2xx once authenticated |

Admin: `GET /api/admin/feedback` (filters + paging), `GET /api/admin/feedback/facets`,
`GET /api/admin/feedback/{id}`, `POST /api/admin/feedback/status`,
`DELETE /api/admin/feedback?id=|verdict=` (deletes the replies first — the FK is `Restrict`).

Feedback channel configuration rides along on `GET/PUT /api/admin/settings` under `feedbackBot`
(`enabled` writable; `telegramConfigured` and `webhook` read-only), plus
`POST /api/admin/settings/feedback/webhook` to register the webhook at the calling origin.

### Admin

Admin CRUD/reporting lives under `/api/admin/*` (all `Admin`-role): `stations`, `fuel-types`,
`packages`, `providers`, `vouchers`, `fuel-vouchers`, `voucher-imports`, `qr-codes`, `orders`
(incl. `POST /api/admin/orders/{id}/refund`), `users`, `settings`, `report`, `audit`, `errors`,
`feedback`, `legal-entity/contracts`, and `dashboard` (aggregated counts). See
`docs/RECONCILIATION.md` in the private docs repo for the reconciliation-specific endpoints.

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
npm start -- --ios --private-key-path ./keys/private-key.pem   # or --android
```

> **Physical device:** `localhost` is not reachable from a physical device. Use the machine's
> LAN IP (e.g. `http://192.168.x.x:5202`) or the production URL.

> **Why `--private-key-path` is required:** `app.json` ships `expo.updates.enabled=true` together
> with a `codeSigningCertificate`, and `expo start` signs a development manifest for it. The CLI
> does not look for the key on its own — with the certificate configured but no flag it aborts
> with:
>
> ```text
> CommandError: Must specify --private-key-path argument to sign development manifest
> for requested code signing key
> ```
>
> `./keys/private-key.pem` is the same key `npm run publish:ota` signs real updates with. It is
> gitignored and lives only on the publish machine; generate it once with
> `npx expo-updates codesigning:generate` (see `docs/DEPLOYMENT.md` in the private docs repo →
> "Over-the-air updates (OTA)"). Only pass it if that file exists — a checkout without the key cannot run a dev server.

> **No `.env`?** Not a problem, and nothing gets hardcoded: `resolveApiBaseUrl()` in
> `mobile/src/core/api/apiClient.ts` reads `EXPO_PUBLIC_API_URL`, then `expo.extra.apiUrl` from
> the committed `app.json` (production), and only falls back to a local dev host if that is
> absent too.

### Database migrations

Migrations **auto-apply on startup** by default (`RunMigrationsOnBoot`, code default `true`).
Set `RunMigrationsOnBoot=false` to manage them manually:

```bash
cd backend/src/FuelFlow.API
dotnet ef database update
```

#### The history is append-only — never squashed

Every migration that has ever been applied to an environment stays in the folder, in order, with
its original id. **Do not collapse them into one baseline migration**, and do not
`dotnet ef migrations remove` a migration a deployed database has already applied.

That is not a style preference. Three things here are built on the history being intact, and each
breaks if it is rewritten:

1. **Production is dry-run from a restored history, not from zero.** The migration dry run
   (`.github/workflows/ci.yml`) dumps production's `__EFMigrationsHistory` and schema, restores
   them into a throwaway Postgres, and applies *only the pending* migrations. A squashed history
   carries an id production has never seen, so EF tries to create every table on a schema that
   already has them — and the one gate that catches a migration which is wrong about
   **production's rows** rather than about seeded ones dies on the first `CREATE TABLE`.
2. **A failing migration is a crash, not a rollback.** `MigrateDatabaseOnStartup` rethrows
   (`Extensions/DatabaseSetup.cs`), so a migration that cannot apply takes the container down and
   the deploy stops there. The rollback for a bad migration is *restore a dump taken before the
   deploy* (`docs/DEPLOYMENT.md` in the private docs repo), never a down-migration — and the dump
   carries the history table with it, so restoring rewinds the history and replays from there.
3. **The data migrations live in the history.** Several of these transform production rows rather
   than describe a shape: `SplitFuelPackagesIntoOffers`, `MoneyIsDecimalOnly`,
   `RefundsBecomeAViewOverPayments`, the `DataKind` backfill. `DataKindBackfillTests` proves that
   backfill by migrating *to* `20261014225424_AddOrderHistoryAndPricingBasis` and asserting what
   the rows look like at that point — possible only because that migration still exists under that
   id.

Collapsing the history is defensible in exactly one situation: **no database anywhere has applied
these migrations yet** — before the first deploy, and only then. This project deployed in June
2026, so that window closed at the first production boot.

What guards the history instead:

| Guard | Catches |
|---|---|
| `has-pending-model-changes` (CI, backend job) | a model change committed with no migration, so the snapshot is never left behind the model |
| `MigrationDiscoveryTests.The_Migration_History_Is_Append_Only` | the history being squashed, or its oldest entries deleted |
| `MigrationDiscoveryTests` (the rest of the class) | a migration EF cannot see — missing `[Migration]`/`[DbContext]`, or living outside the one folder |
| `XminMigrationGuardTests` | a scaffold that would crash every boot (the `xmin` system column) |
| the production dry run | a migration that applies cleanly to seeded data and is still wrong about production's rows |
| `deploy/preflight-db` | production rows a migration cannot safely transform, asked *before* the pull |

Adding a migration is the normal case and is covered by `AGENTS.md`: run
`dotnet ef migrations add <Name> --project src/FuelFlow.API`, review the generated `Up`/`Down`
(the scaffold is a starting point, not an answer — see `XminMigrationGuardTests` for a scaffold
that looked fine and was not), and commit it. The CI step above fails the PR if you forget.

### Running the backend tests

```bash
cd backend
dotnet test tests/FuelFlow.UnitTests/FuelFlow.UnitTests.csproj
dotnet test tests/FuelFlow.IntegrationTests/FuelFlow.IntegrationTests.csproj
```

Nothing needs to be running first. The integration suite starts its own containers: one Postgres for
the whole suite (migrated once, then cloned per test class) and one Redis. **Redis is not optional
there** — the API resolves `ConnectionStrings:Redis` and falls back to `localhost:6379`, so a test
host without Redis does not fail, it *stalls*: every request that touches the device-signature
nonce cache waits out StackExchange.Redis' 5 s timeout and then throws. That was 92% of the suite's
wall time before the fixture supplied a container. The container is configured with production's
`--maxmemory 128mb --maxmemory-policy noeviction` on purpose — see `TestDatabaseFixture.cs`.

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
- **`Telegram:Feedback:*`** - the feedback bot (own token, own staff group, own `WebhookSecret`, `SigningKey`, `StaffUserIds`); off by default, and the admin Settings screen reports whether it is actually configured rather than assuming. The webhook is registered from there (`POST /api/admin/settings/feedback/webhook`), which is what you re-run after a redeploy or a domain change. `Telegram:*` beside it is the unrelated **alerting** bot - two tokens on purpose, because Telegram allows one webhook per token.
- **`Llm:*`** - the feedback triage classifier (API key + model); off by default, and with it off every message is stored and shown unfiltered rather than lost.
- **`Monobank:PublicKey`** — PEM (or base64-of-PEM) used to verify webhook signatures; production fails fast if `Monobank:Enabled=true` with a placeholder key.

Mobile (`mobile/.env`): `EXPO_PUBLIC_API_URL=...` · Admin (build arg in `admin/Dockerfile` / dev env): `VITE_API_URL=...`

Production values for all of the above live in `deploy/.env` on the server (see
`docs/DEPLOYMENT.md` in the private docs repo).

---

## Deployment

- **Everything** (API, admin, website, Postgres, Redis) runs on a single Hetzner server
  via Docker Compose (`deploy/docker-compose.prod.yml`), Caddy for TLS at the edge.
  Deploys are **automatic**: every green merge to `main` is built into images in CI (pushed
  to GHCR), rolled out to a same-box staging stack and smoke-tested, then deployed to prod,
  which **pulls** those exact SHA-pinned images (self-hosted runner). Full runbook:
  `docs/DEPLOYMENT.md` in the private docs repo.
- **Mobile** — Expo / EAS (development builds + TestFlight); web export available.

Set sensitive values (`Database__ConnectionString`, `Jwt__Secret`, `Monobank__Token`,
`Monobank__WebhookUrl`, `Monobank__PublicKey`) in `deploy/.env` on the server — never committed.

---

## Documentation index

The operational documentation is **not in this repository**. It lives in the private
[`fuel-voucher-platform-planning`](https://github.com/art-of-v/fuel-voucher-platform-planning)
repo under `docs/` — it describes the live host, the DNS zone, the alerting stack and the
operational procedures, and this repository is public. References like `docs/DEPLOYMENT.md`
in code comments and scripts throughout this repo point at that private repo.

| Doc (private repo) | Contents |
|---|---|
| `docs/DEPLOYMENT.md` | Deploy & ops runbook: Hetzner stack, CI auto-deploy, `deploy/.env` variables, backups, troubleshooting |
| `docs/ROTATION.md` | Rotating a secret on the box — the procedure, and the traps that cost two outages |
| `docs/DNS.md` | Authoritative DNS for `palne.shop` — the live zone, and what was verified about the Cloudflare cutover vs what was not |
| `docs/SECURITY.md` | Auth & device-binding model (plain-language + the real implemented controls) + fraud-analysis findings |
| `docs/DESIGN.md` | Mobile design system (tokens, components, rules) + app structure & core flows |
| `docs/MOBILE-ARCHITECTURE.md` | How the mobile code is organised: where new code goes, layer rules, data flow, non-obvious constraints, known debt |
| `docs/MOBILE-MAINTAINABILITY.md` | Maintainability audit of the mobile app + the plan to address it (living tracker) |
| `docs/RECONCILIATION.md` | Admin & customer reconciliation, refunds, SQL queries |
| `docs/COMPANY_WORKERS.md` | Company owner/worker feature (data model + `/api/company` API) |
| `docs/OBSERVABILITY.md` | Prometheus/Grafana/Loki observability stack + Telegram alerting + Sentry error tracking |
| `docs/TESTING.md` | How the test suite is wired, and what each layer actually proves |
| `docs/FEEDBACK_BOT.md` | In-app feedback channel: the Telegram bot, its LLM triage, the two-way staff relay, and why each gate exists |
| `docs/WORKFLOW.md` | Branching, PR and merge conventions |

They were removed from this repository on 2026-10-12; the git history still contains them.

---

## Known limitations

| Area | Issue |
|---|---|
| **OTP dev bypass** | With `Auth:DevBypass=true`, code `000000` works for any phone and SMS is faked. Keep it `false` in production. |
| **Synchronous PDF import** | Import runs inside the request; very large PDFs can approach the client timeout. Bounded by an import-concurrency guard. |
| **Self-reported redemption** | `mark-used` is honor-system — there is no POS/pump integration proving fuel was dispensed. |

See `docs/SECURITY.md` in the private docs repo for the fraud-analysis findings and the
open/closed work packages.
