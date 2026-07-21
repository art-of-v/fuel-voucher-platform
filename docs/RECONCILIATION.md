# FuelFlow Reconciliation Guide

Covers reconciliation processes for **admins** (via web dashboard + database) and **customers** (via mobile app).

---

## Table of Contents

1. [Glossary](#glossary)
2. [Admin Reconciliation](#admin-reconciliation)
   - [Dashboard Overview](#dashboard-overview)
   - [Voucher Inventory Reconciliation](#voucher-inventory-reconciliation)
   - [Order & Fulfillment Reconciliation](#order--fulfillment-reconciliation)
   - [Financial Reconciliation](#financial-reconciliation)
   - [Background Job Monitoring](#background-job-monitoring)
   - [SQL Queries for Deep Reconciliation](#sql-queries-for-deep-reconciliation)
   - [Reconciliation Schedule](#reconciliation-schedule)
3. [Customer (Mobile App) Reconciliation](#customer-mobile-app-reconciliation)
   - [My Codes Screen](#my-codes-screen)
   - [Pending Orders](#pending-orders)
   - [How to Verify Fulfillment](#how-to-verify-fulfillment)
   - [Reporting Discrepancies](#reporting-discrepancies)
4. [API Endpoints Reference](#api-endpoints-reference)

---

## Glossary

| Term | Definition |
|---|---|
| **Order** | A customer's purchase request. Contains product, quantity, price, Monobank invoice reference. |
| **Voucher** | A single fuel voucher unit (imported from PDF). Assigned to orders via fulfillments. |
| **Fulfillment** | The junction record linking an order to the vouchers that satisfy it. |
| **Outbox Event** | An internal event log entry (`OrderCreated`, `OrderFulfilled`, etc.). Processed by Hangfire background jobs. |
| **PendingFulfillment** | Order status meaning payment succeeded but vouchers haven't been assigned yet (awaiting inventory). |
| **FEFO** | First Expiry, First Out — voucher assignment strategy (oldest-expiring vouchers used first). |

---

## Admin Reconciliation

### Dashboard Overview

The admin dashboard at `GET /api/admin/dashboard` provides high-level reconciliation counts:

```json
{
  "users": { "total": 42 },
  "vouchers": {
    "total": 1500,
    "available": 1200,
    "assigned": 200,
    "used": 100,
    "verificationFailed": 0,
    "verifiedWithWarnings": 0,
    "byProvider": [
      { "provider": "okko", "count": 1000 },
      { "provider": "wog", "count": 500 }
    ]
  },
  "orders": {
    "total": 180,
    "pending": 5,
    "fulfilled": 170,
    "revenueUah": 340000
  }
}
```

**Key reconciliation checks at a glance:**

| Check | Formula | Expected |
|---|---|---|
| Total vouchers | `available + assigned + used + failed` | Equals imported count |
| Fulfilled orders | Should match user vouchers | `assigned` ≈ sum of vouchers in fulfilled orders |
| Revenue | `SUM(price)` of fulfilled + partially fulfilled orders | Monobank settlement total |
| Pending queue | `PendingFulfillment` + `PartiallyFulfilled` orders | Should trend to 0 if vouchers are available |

---

### Voucher Inventory Reconciliation

**Objective:** Ensure every imported voucher is accounted for — available, assigned to a customer, used, or expired.

**Available tools:**
- **Admin Dashboard** — voucher counts by status and provider
- **Vouchers Table** (admin panel) — paginated, filterable, sortable list of all vouchers
- **Imports Tab** — batch-level view of import jobs with per-voucher breakdown

**Reconciliation steps:**

1. **Check total vs. by-status sum**
   ```
   Total = Available + Assigned + Used + VerificationFailed + VerifiedWithWarnings
   ```
   If this doesn't balance, some vouchers may be stuck in `Imported` status (not yet activated).

2. **Review imports with high error/warning rates**
   - In the Imports tab, check `verificationMismatchPercent` and `failedCount`
   - Drill into import details to see individual failures
   - Vouchers with `VerificationFailed` status may need manual review

3. **Filter by provider**
   - Use the provider filter to compare counts by station brand (OKKO, WOG, UPG, KLO)
   - Cross-reference with physical inventory sheets from each station

4. **Identify stale vouchers**
   - Filter by `Imported` status — these haven't been activated. Decide to activate or delete.
   - Check expiration dates — expired vouchers should not be fulfilling orders (see [Known Limitation](README.md) — expiration check is currently disabled).

---

### Order & Fulfillment Reconciliation

**Objective:** Verify that every paid order has been fulfilled (or has a reason for not being fulfilled).

**Reconciliation flow:**

```
┌─────────────┐     ┌──────────────┐     ┌───────────────┐
│ User places  │────▶│ Monobank     │────▶│ Order →       │
│ order        │     │ payment      │     │ PendingFulfill │
└─────────────┘     └──────────────┘     └───────┬───────┘
                                                 │
                      ┌──────────────────────────┘
                      ▼
            ┌─────────────────────┐
            │ FulfillmentService  │  (Hangfire, runs every 1 min)
            │ Assigns vouchers    │
            │ (FEFO strategy)     │
            └──────────┬──────────┘
                       │
              ┌────────┴────────┐
              ▼                 ▼
      ┌────────────┐    ┌──────────────┐
      │ Fulfilled   │    │ Partially    │
      │ (all goods) │    │ Fulfilled    │
      └────────────┘    └──────────────┘
```

**Steps:**

1. **Get all orders** via `GET /api/admin/orders`
   - Review orders with status `PendingFulfillment` — these are stuck and need attention
   - Review `PartiallyFulfilled` orders — may need manual voucher assignment

2. **For each pending order**, determine root cause:
   - Insufficient voucher inventory? → Import more vouchers
   - Fulfillment job stalled? → Check Hangfire dashboard at `/hangfire`
   - Voucher matching failure? → Check if provider/fuel type/liters match

3. **Cross-reference fulfillments**
   ```
   For each Fulfilled order:
     SUM(voucher.liters) across all assigned vouchers = order.liters * order.quantity
     COUNT(vouchers) = order.quantity
   ```
   Use the per-order detail endpoint: `GET /api/admin/orders/{id}`

4. **Run the sync endpoint** to see the customer's view:
   ```
   GET /api/sync/orders (as user)
   GET /api/vouchers/my (as user)
   ```

---

### Financial Reconciliation

**Objective:** Ensure payments received via Monobank match orders and fulfillments.

**Available data:**

| Field | Source |
|---|---|
| `MonobankInvoiceId` | Order entity — Monobank invoice reference |
| `MonobankStatus` | Order entity — `Pending`, `Success`, `Failure`, `Cancelled`, `Expired` |
| `Price` | Order entity — price in UAH kopecks (e.g., 100000 = 1000 UAH) |

**Reconciliation steps:**

1. **Revenue verification**
   ```
   Total Revenue (dashboard) = SUM(price) for Fulfilled + PartiallyFulfilled orders
   ```
   Compare this against Monobank merchant dashboard settlement reports.

2. **Identify payment mismatches**
   - Orders with `MonobankStatus: Success` but `Status: PendingPayment` — indicates webhook delivery failure. Check Hangfire logs and the Monobank webhook controller.
   - Orders with `Status: Fulfilled` but `MonobankStatus: null` — likely simulated payments during testing.

3. **Audit outbox events**
   The outbox event log provides a complete audit trail:
   ```sql
   SELECT * FROM outbox_events
   WHERE event_type IN ('PaymentCompleted', 'OrderCreated', 'OrderFulfilled')
   ORDER BY created_at_utc;
   ```
   Each event has a JSON payload with the order/voucher IDs.

---

### Background Job Monitoring

The Hangfire dashboard is available at `/hangfire` (admin auth required).

**Two critical recurring jobs:**

| Job | Schedule | What it does |
|---|---|---|
| `process-fulfillments` | Every 1 min | Reads `OrderCreated` outbox events + backfills open orders, assigns vouchers |
| `process-notifications` | Every 1 min | Creates in-app notifications after fulfillment |

**Use the dashboard to:**
- Check if jobs are failing or taking too long
- Retry failed jobs after fixing the root cause
- View execution history for each job
- Monitor queue length

---

### SQL Queries for Deep Reconciliation

For manual database-level reconciliation (connect to your PostgreSQL instance):

**1. Orders by status with fulfillment counts**
```sql
SELECT
  o.status,
  COUNT(*) AS order_count,
  COUNT(f.id) AS fulfillment_count,
  SUM(o.price) AS total_price_kopecks
FROM orders o
LEFT JOIN fulfillments f ON f.order_id = o.id
GROUP BY o.status
ORDER BY o.status;
```

**2. Orphans — vouchers assigned but not linked to any order**
```sql
SELECT v.id, v.voucher_number, v.status, v.assigned_to_user_id
FROM fuel_vouchers v
LEFT JOIN fulfillments f ON f.voucher_id = v.id
WHERE v.status = 'Assigned' AND f.id IS NULL;
```

**3. Unfulfilled pending orders**
```sql
SELECT o.id, o.user_id, o.provider, o.fuel_type_id, o.liters, o.quantity,
       o.created_at_utc, o.monobank_status
FROM orders o
WHERE o.status IN ('PendingFulfillment', 'PartiallyFulfilled')
ORDER BY o.created_at_utc DESC;
```

**4. Voucher inventory by provider + fuel type**
```sql
SELECT provider, fuel_type_id, status, COUNT(*) AS count,
       SUM(liters) AS total_liters
FROM fuel_vouchers
GROUP BY provider, fuel_type_id, status
ORDER BY provider, fuel_type_id, status;
```

**5. Customer order history with voucher detail**
```sql
SELECT
  o.id AS order_id, o.status AS order_status,
  o.created_at_utc, o.price, o.liters, o.quantity,
  v.voucher_number, v.status AS voucher_status, v.liters AS voucher_liters,
  f.fulfilled_at_utc
FROM orders o
JOIN fulfillments f ON f.order_id = o.id
JOIN fuel_vouchers v ON v.id = f.voucher_id
WHERE o.user_id = '<user-uuid>'
ORDER BY o.created_at_utc DESC;
```

**6. Revenue reconciliation by month**
```sql
SELECT
  DATE_TRUNC('month', o.fulfilled_at_utc) AS month,
  COUNT(*) AS fulfilled_orders,
  SUM(o.price) AS revenue_kopecks,
  SUM(o.price) / 100.0 AS revenue_uah
FROM orders o
WHERE o.status = 'Fulfilled'
GROUP BY DATE_TRUNC('month', o.fulfilled_at_utc)
ORDER BY month DESC;
```

**7. Outbox event audit trail**
```sql
SELECT
  created_at_utc,
  event_type,
  payload,
  processed,
  processed_at_utc
FROM outbox_events
WHERE created_at_utc >= NOW() - INTERVAL '30 days'
ORDER BY created_at_utc DESC;
```

---

### Reconciliation Schedule

| Frequency | Activity | Responsible |
|---|---|---|
| **Daily** | Check dashboard for pending orders (`PendingFulfillment` + `PartiallyFulfilled`) | Admin |
| **Daily** | Verify Hangfire dashboard for job failures | Admin |
| **Weekly** | Voucher inventory count by provider and status | Admin |
| **Weekly** | Review imports with errors/warnings | Admin |
| **Monthly** | Full financial reconciliation — orders vs. Monobank settlements | Finance |
| **Monthly** | Outbox event log review for anomalies | Admin |
| **On-demand** | After large PDF imports, verify counts match expected inventory | Admin |

---

## Customer (Mobile App) Reconciliation

### My Codes Screen

The **My Codes** screen (`/my-codes` in the mobile app) is the customer's primary reconciliation tool. It shows:

**1. Active Vouchers** — vouchers assigned to the customer that are ready for use at stations.
- Each voucher shows: provider, fuel type, liters, voucher number, expiration date, QR code
- Statuses: `Assigned` (usable) or `Used` (already redeemed)

**2. Pending Orders** — orders that have been paid but not yet fulfilled (no vouchers assigned yet).
- These display with a `PENDING_FULFILLMENT` status indicator
- The customer can see these are awaiting voucher assignment

**3. Complete Orders** — orders that have been fulfilled (all vouchers assigned).

### Data Sources

The screen fetches two API endpoints in parallel:

| Endpoint | Returns | Purpose |
|---|---|---|
| `GET /api/vouchers/my` | Customer's assigned/used vouchers with QR data | Show active inventory |
| `GET /api/sync/orders` | Customer's orders with embedded voucher details | Show order history + fulfillment status |

**Status mapping (backend → mobile):**

| Backend OrderStatus | Mobile Display |
|---|---|
| `PendingPayment` | `PENDING_FULFILLMENT` |
| `Paid` | `PENDING_FULFILLMENT` |
| `PendingFulfillment` | `PENDING_FULFILLMENT` |
| `PartiallyFulfilled` | `PENDING_FULFILLMENT` |
| `Fulfilled` | `FULFILLED` |
| `Refunded` | `REFUNDED` |
| `Cancelled` | `REFUNDED` |

### How Customers Can Verify Reconciliation

**1. Order → Voucher match**
For any fulfilled order, the customer sees exactly which vouchers were assigned to it. The number of vouchers × liters should equal the order quantity × liters.

**2. Active vouchers count**
```
Expected vouchers = SUM(quantity) of all Fulfilled orders - vouchers marked as Used
```
The customer can cross-reference this manually from the My Codes list.

**3. Check for missing vouchers**
If an order shows `FULFILLED` but no matching voucher appears, the customer should:
- Pull-to-refresh the My Codes screen (triggers new API calls)
- Check order status shows `FULFILLED` (not `PENDING_FULFILLMENT`)

**4. Voucher usage history**
Vouchers can be toggled to `Used` status from the mobile app (mark-as-used). This is a client-driven action — the customer marks a voucher as used after redeeming it at a station. The app also supports **restore** (admin-only) if a voucher was accidentally marked as used.

### Reporting Discrepancies

**What to do if something doesn't match:**

| Issue | Customer Action | Admin Resolution |
|---|---|---|
| Order shows `PENDING_FULFILLMENT` for >30 min | Wait for background job (runs every 1 min) or contact support | Check voucher inventory; check Hangfire dashboard |
| Voucher missing from My Codes after order fulfilled | Pull-to-refresh; if still missing, contact support | Verify fulfillment in `GET /api/admin/orders/{id}` |
| Wrong voucher amount/liters assigned | Contact support | Check FEFO assignment logic; may need manual adjustment |
| Payment taken but order not created | Contact support (provide Monobank receipt) | Search by `MonobankInvoiceId`; check webhook logs |
| Voucher wrongly marked as used | N/A (customer action — can't be undone by customer) | Admin can use `PATCH /api/vouchers/{id}/restore` |

**Customer support contact should include:**
- Order ID (UUID) — visible on the My Codes screen
- Screenshot of the order/voucher status
- Monobank payment receipt (if applicable)

---

## API Endpoints Reference

### Admin Reconciliation Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/admin/dashboard` | Admin | Aggregated counts: users, vouchers (by status, provider), orders, revenue |
| `GET` | `/api/admin/orders` | Admin | All orders with fulfillment status |
| `GET` | `/api/admin/orders/{id}` | Admin | Single order detail |
| `GET` | `/api/admin/vouchers` | Admin | Paginated, filterable voucher list |
| `GET` | `/api/admin/vouchers/{id}` | Admin | Single voucher detail |-- |
| `GET` | `/api/admin/voucher-imports` | Admin | Import batch history |
| `GET` | `/api/admin/voucher-imports/{id}/vouchers` | Admin | Vouchers within a specific import batch |
| `GET` | `/hangfire` | Admin | Hangfire dashboard (job monitoring) |

### Customer Reconciliation Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/vouchers/my` | User | User's assigned/used vouchers with QR |
| `GET` | `/api/purchases/my` | User | User's purchase list |
| `GET` | `/api/sync` | User | Combined sync response (orders + totals) |
| `GET` | `/api/sync/orders` | User | User's orders with embedded voucher details |

### Internal Audit Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/health` | None | Health check |
| `POST` | `/api/purchases/simulate` | Admin | Simulate Monobank payment (for testing reconciliation) |

---

## Known Reconciliation Gaps

| Gap | Impact | Workaround |
|---|---|---|
| No `GET /api/admin/purchases` endpoint | Admin panel purchases tab may not load data | Use `GET /api/admin/orders` directly via API or database queries |
| No CSV/Excel export | Data must be reconciled manually or via SQL | Use the SQL queries in this guide |
| No automated discrepancy alerts | Issues may go unnoticed until manual check | Monitor Hangfire dashboard daily |
| Expiration check disabled for vouchers | Expired vouchers may be assigned to orders | Track manually via SQL query and bulk-expire vouchers |
