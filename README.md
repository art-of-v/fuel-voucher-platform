# FuelFlow

A full-stack fuel-voucher platform. End users buy fuel (in liters) at partner stations
(OKKO, WOG, KLO) from a native mobile app, pay via Monobank, and receive digital vouchers
they redeem at the pump. Administrators import physical voucher stock from PDF catalogs,
manage pricing and inventory, and reconcile payments against fuel delivered — all from a
web dashboard.

Three applications share one backend:

| App | Stack | Purpose |
|---|---|---|
| [`mobile/`](mobile) | React Native 0.81 · Expo 54 · Expo Router · NativeWind · Zustand · TanStack Query | End-user app: map, stations, packages, basket, checkout, payments, vouchers, referrals, profile |
| [`admin/`](admin) | React 19 · Vite 7 · TypeScript · shadcn/ui · Tailwind 4 · TanStack Query · Zustand · Recharts | Back-office: voucher import, providers/pricing, users, contracts, reports, audit & error logs |
| [`backend/`](backend) | .NET 10 · ASP.NET Core · EF Core 10 · PostgreSQL (Npgsql) · Hangfire · Redis | REST API, background jobs, Monobank integration, auth, outbox |

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

The backend is the single source of truth; both frontends are pure API clients. Money-affecting
values are computed and enforced server-side — a client only ever *proposes* a price or a
status. Orders are gated on voucher availability, a voucher in somebody's hands stays bound to
the order that delivered it, background jobs run in-process through Hangfire, domain events
travel over a transactional outbox, and every domain/admin change is audited.

The layering is a gate, not a convention. `backend/tests/FuelFlow.UnitTests/Architecture/DependencyRules/`
reads the **compiled** API assembly and fails the build when a controller reaches the database,
when a handler reaches for the HTTP context, or when a feature uses another feature's internal
types — and a source scan fails it when a failure leaves the API in any shape but the one below.

Every failure — a controller's rejection, the exception handler's answer, a middleware that
refuses a request — leaves as one RFC 9457 problem document served as `application/problem+json`:
the status in `title`, the sentence for the caller in `detail`, the request path in `instance`,
the correlation id in `traceId`, and the machine code (with its numbers) in `code` and `data`.
Both frontends read that one shape; neither of them guesses which one it got.

---

## Repository structure

```
FuelFlow/
├── admin/                    # React 19 admin dashboard (Vite + Tailwind 4 + shadcn/ui)
│   └── src/pages/admin/      # One component per screen (users, purchases, vouchers, imports,
│                             #   reconciliation, reports) behind the session gate
├── backend/                  # .NET 10 ASP.NET Core API + standalone jobs worker
│   ├── Directory.Build.props # The MSBuild settings every project shares
│   └── src/
│       ├── FuelFlow.API/
│       └── FuelFlow.JobsWorker/
├── mobile/                   # Expo React Native app
│   ├── app/                  # Expo Router screens (index, landing, packages, basket,
│   │                         #   checkout, my-codes, map, profile, company/…)
│   └── src/                  # components, hooks, features, core (api, i18n, store)
├── website/                  # Next.js marketing site behind palne.shop (static export)
├── deploy/                   # Production stack (compose, Caddyfile, backup/restore)
├── scripts/                  # Repository maintenance helpers
├── .github/                  # CI workflows
├── .editorconfig             # Editor conventions for the whole repository
├── global.json               # The pinned .NET SDK
├── docker-compose.yml        # Full local stack
└── .env.example              # Template for required environment variables
```

---

## Quick start

### Full stack via Docker Compose

```bash
cp .env.example .env
# The API refuses to start without the voucher QR key. Generate one and put it in .env:
#   openssl rand -base64 32
docker compose up --build
```

The API answers on `http://localhost:5000`, the admin SPA on `http://localhost:5002` — the admin
proxies `/api` to the API, so the browser sees one origin. The mobile app is **not** part of this
stack: it is an Expo app and runs on a device or a simulator (`npm start` in `mobile/`).

CI brings this same stack up and asserts that the API is healthy
(`scripts/check-local-stack.sh`), so a broken compose file is a red build rather than a surprise
on a new machine.

### Backend only

```bash
cd backend/src/FuelFlow.API
dotnet run
```

The API listens on `http://localhost:5202` (`Properties/launchSettings.json`). Hangfire is at
`/hangfire`, Swagger at `/swagger`. It still needs a PostgreSQL and a Redis — the compose stack
above provides both (`docker compose up -d admin-database redis`), and the API connects to the
same database the stack creates.

### Admin frontend

```bash
cd admin
npm install
npm run dev        # Vite dev server on :5173, proxies /api → localhost:5202
```

The admin runs the same four checks as the mobile app, and CI runs them on every pull request:

```bash
npm run typecheck     # tsc --noEmit
npm run lint          # ESLint — `any` is an error, and a warning fails the gate
npm run format:check  # Prettier
npm run test          # Vitest
```

`npm run lint:fix` and `npm run format` fix what can be fixed automatically.

### Mobile app

```bash
cd mobile
npm install
echo "EXPO_PUBLIC_API_URL=http://localhost:5202" > .env
npm start -- --ios --private-key-path ./keys/private-key.pem   # or --android
```

> `localhost` is not reachable from a physical device — use the machine's LAN IP or the
> production URL. `--private-key-path` is required because the committed `app.json` ships an Expo
> code-signing certificate; a checkout without the key under `mobile/keys/` omits the flag.

### JavaScript dependencies

CI fails the build on a known advisory in `admin/` or `mobile/` that is not on the accepted-risk
list — the counterpart of the NuGet scan the backend job runs:

```bash
node scripts/npm-audit-gate.mjs admin mobile   # what CI runs
node --test scripts/npm-audit-gate.test.mjs     # the gate's own tests
```

The accepted advisories live in `scripts/npm-audit-accepted-risk.json`, and every entry carries
the path the advisory reaches this repository through, the fix (or the statement that there is
none), and the reason the risk is accepted. Adding an advisory to that list is a decision: write
the reason, or the next reader has a number instead of a decision.

When the gate blocks, its output is the decision's starting point: the advisory, the version the
lockfile resolves, the affected range, the patched version with the one command that installs it,
and — when the lockfile marks the package dev-only — the fact that it is build-time (the toolchain,
not the app the customer installs). That means the common case, a published advisory on a direct
dependency, is a one-line bump read off the failure rather than a trip to the advisory page.

The `overrides` blocks in `admin/package.json` and `mobile/package.json` pin the transitive
dependencies whose advisory has a fix, so a lockfile refresh cannot quietly bring one back.

### Database migrations

Migrations auto-apply on startup (`RunMigrationsOnBoot`, default `true`); to apply them by hand:

```bash
cd backend/src/FuelFlow.API
dotnet ef database update
```

**The migration history is append-only — never squashed into one baseline.** The full reasoning
is in `docs/CODEBASE.md` in the private docs repo.

---

## Documentation

The technical and operational documentation is **not in this repository**. Architecture
decisions, key flows, the database schema, the API reference, the money/precision rules, the
deploy runbook and the known limitations live in the private
[`fuel-voucher-platform-planning`](https://github.com/art-of-v/fuel-voucher-platform-planning)
repo under `docs/`:

- `docs/CODEBASE.md` — the codebase reference: architecture, key flows, schema, API reference,
  environment variables. It was this README until 2026-10-18.
- `docs/DEPLOYMENT.md` — deploy & ops runbook for the Hetzner stack, CI auto-deploy, backups,
  troubleshooting.

The **app-side operator guides** live in this repository under `docs/`, because they describe the
running app rather than the platform's design:

- `docs/SUPPLIER_REDEMPTION_RECONCILIATION_RUNBOOK.md` — the daily supplier statement import and the
  redemption exception queue.
- `docs/PAID_ORDER_NO_VOUCHER_RUNBOOK.md` — a paid order with no voucher: the match predicate, the
  triage queries, top-up and refund, and the deferred manual release.
- `docs/APP_ATTESTATION_AND_PINNING_RUNBOOK.md` — attestation and certificate pinning.
- `docs/DEVICE_SIGNATURE.md` — the device signature contract.

References like `docs/DEPLOYMENT.md` in code comments and scripts throughout this repo point at
that private repo.