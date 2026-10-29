# Paid order, no free voucher — operator runbook

A customer has paid and the order is still `PendingFulfillment` (or `PartiallyFulfilled`) because
no matching voucher exists. This is the P1 shape of planning **#781**: money taken, fuel not
delivered, discovered at the counter by the person who has already paid.

The related operator guides live in this repo:
`docs/SUPPLIER_REDEMPTION_RECONCILIATION_RUNBOOK.md` (the `PendingReconciliation` queue) and the
planning repo's `docs/VOUCHER_LIFECYCLE.md`, `docs/STOCK_TOPUP.md`.

## What "a matching voucher" means

`FulfillmentService.FindMatchingVouchersAsync` matches a line on **all** of:

| Predicate | Value for a real customer |
|---|---|
| `status` | `Available` |
| `brand` | the order line's brand, exact text |
| `fuel_type_id` | the grade's id — not its name, not a "compatible" grade |
| `liters` | the pack size; **a 10 L line cannot be filled from a 20 L voucher** |
| `data_kind` | `real` for a paying customer, `qa` for the QA test account |
| `supplier_expiration_date` | today or later (unless `VoucherExpiration:Enabled` is off) |
| `is_deleted` | false |

The candidates are then claimed in expiry order by a compare-and-swap `UPDATE`, so two orders never
take the same voucher.

A total across sizes, or a count that includes QA stock, is **not** this answer. That is exactly how
#781 happened: the brand/fuel total read healthy while the size the customer had ordered was empty.
The pool metric and the low-stock alert now report the same grain this query does — brand, fuel,
size, real stock.

## The alerts that should speak first

| Signal | Where | Fires at |
|---|---|---|
| `Малий залишок талонів` | Telegram, from `VoucherStockMonitor` (Hangfire) | `available <= LowLevelThreshold` (20) per brand / fuel / size / real |
| `Талонів не залишилось` | Telegram, same job | `available = 0` for a combination |
| `VoucherPoolLow` | Grafana | the gauge is below 10 for a series |
| `VoucherPoolMetricAbsent` | Grafana | the gauge has no series at all — the exporter, not the stock |
| `OrderUnfulfillable` | Telegram, from the fulfilment worker | a paid order could not be filled |

All of these are per combination. A message that names only the brand and the fuel is not enough
to act on: buy the size it names.

## Step 1 — read, do not write

Connect to the database read-only and answer four questions. `now()` is safe here: every
`*_at_utc` column is `timestamp with time zone`. The `:'name'` placeholders are psql variables —
set them with `\set` or paste the ids in.

```sql
-- 1. Is the order actually paid, and what state is it in?
--    A capture is the proof of money; PendingPayment is not a "no stock" problem.
select o.id, o.status, o.price, o.created_at_utc, o.fulfilled_at_utc
from orders o
where o.id = :'order';

select p.kind, p.status, p.amount_kopecks, p.paid_at_utc
from payments p
where p.order_id = :'order'
order by p.created_at_utc;
```

```sql
-- 2. What does the order still need, line by line?
select li.id as line_item_id, li.brand, ft.name as fuel, li.liters, li.quantity,
       (select count(*) from fulfillments f
         where f.order_id = o.id and f.line_item_id = li.id and f.released_at_utc is null) as delivered
from order_line_items li
join orders o on o.id = li.order_id
left join fuel_types ft on ft.id = li.fuel_type_id
where o.id = :'order';
```

```sql
-- 3. Can the pool satisfy one line?  This is the fulfilment predicate, nothing added.
select v.liters, count(*) as matching_vouchers,
       min(v.supplier_expiration_date) as first_to_expire
from fuel_vouchers v
where v.status = 'Available'
  and v.brand = :'brand'
  and v.fuel_type_id = :'fuel_type_id'
  and v.liters = :'liters'
  and v.data_kind = 'real'
  and not v.is_deleted
  and v.supplier_expiration_date >= current_date
group by v.liters;
```

```sql
-- 4. What does the brand and grade actually carry?  This is where "34 available" turns out to be
--    "34 of the size nobody ordered".
select v.liters, v.status, v.data_kind, count(*)
from fuel_vouchers v
where v.brand = :'brand' and v.fuel_type_id = :'fuel_type_id' and not v.is_deleted
group by 1, 2, 3
order by 1, 2;
```

If (2) says `delivered = quantity`, the order is not short — check the order status and the
`fulfillments` rows before touching anything. If (3) returns a count that covers the remaining
quantity, the stock exists and the sweep simply has not run yet: wait for the next minute
(`ProcessPendingOrdersAsync`) before doing anything manually.

## Step 2 — the repair that is always right: bring stock in

This is the normal answer, and it needs no database access.

1. **Import** vouchers for that brand, grade and **size**: Admin → Imports, with the supplier and
   the cost per litre. A voucher without a cost cannot be activated — the activation gate refuses
   the whole batch rather than putting uncosted stock on sale.
2. **Activate** the new batch (Admin → Imports → Activate, or the voucher list's bulk action) —
   imported stock is not sellable until it is activated.
3. Activating **enqueues `ProcessPendingOrdersAsync`**. The paid order is picked up on the next
   sweep and becomes `Fulfilled`; the customer's app shows the voucher without any further action.
4. **Verify**:

```sql
select o.status, o.fulfilled_at_utc,
       count(f.id) filter (where f.released_at_utc is null) as live_fulfilments
from orders o
left join fulfillments f on f.order_id = o.id
where o.id = :'order'
group by o.status, o.fulfilled_at_utc;
```

## Step 3 — if the fuel cannot be brought in: refund

If no supplier can deliver that brand / grade / size in time, refund the order from the admin order
screen. The money goes back and the order leaves `PendingFulfillment`.

A refund does **not** take back vouchers that were already delivered: those are the customer's
property now, and taking them back is a different decision (see the reconciliation runbook).

## Step 4 — manual correction of the vouchers (deferred endpoint)

**There is no admin endpoint for assign / return yet**, and bulk `assign` is refused on purpose:
vouchers are issued by fulfilling an order, not by an operator picking one. Planning #781 leaves
this path open as an explicit deferral; until the endpoint exists, this section is the procedure,
and it is also the specification the endpoint must implement.

When a manual correction is legitimate:

- a **duplicate or phantom delivery** — a `fulfillments` row points at a voucher that belongs to
  another line or another order, and the reconciliation left it because the order was older than
  its grace window;
- an order that **will not be delivered** (refunded, cancelled, dead after the auto-refund window)
  and still holds a voucher.

When it is **not** legitimate, and must not be done:

- taking a voucher off **another paid order** to give it to this one — that robs a customer who has
  also paid. Top up or refund instead;
- releasing a **`PendingReconciliation`** voucher — the holder has reported a redemption at a
  station. Handing that paper to somebody else is a double spend. It leaves that state through the
  redemption queue (`SUPPLIER_REDEMPTION_RECONCILIATION_RUNBOOK.md`), not through this section;
- writing `fuel_vouchers.status` alone. A voucher's state, its custody and its delivery are three
  facts owned by three tables (`voucher_events`, `voucher_movements`, `fulfillments`). A repair that
  writes one of them leaves the ledger, the lifecycle drawer and the pool count disagreeing.

The correction, in one transaction, with the operator's own user id and a reason:

```sql
begin;

-- 1. The delivery record is KEPT and marked released (#217): the row says a delivery happened,
--    the release says it was undone. Deleting it destroys the only evidence.
update fulfillments
set released_at_utc = now(), release_reason = 'operator_release'
where voucher_id = :'voucher' and released_at_utc is null;

-- 2. The paper is back in the warehouse.
update fuel_vouchers
set status = 'Available',
    assigned_to_user_id = null,
    legal_entity_id = null,
    worker_user_id = null,
    order_id = null,
    updated_at_utc = now()
where id = :'voucher' and status = 'Assigned';

-- 3. State change, with an author and a reason (ck_voucher_events_actor_named).
insert into voucher_events
    (id, voucher_id, event_type, from_status, to_status, external_ref,
     actor_kind, actor_user_id, reason, occurred_at_utc)
values
    (gen_random_uuid(), :'voucher', 'StatusChanged', 'Assigned', 'Available', :'order',
     'user', :'admin_user', :'reason', now());

-- 4. Custody: customer -> pool. `returned`, because the paper changed hands (D-13).
insert into voucher_movements
    (id, kind, voucher_id, from_location, to_location, order_id, line_item_id,
     actor_kind, actor_user_id, reason, occurred_at_utc)
values
    (gen_random_uuid(), 'returned', :'voucher', 'customer', 'pool', :'order', :'line_item',
     'user', :'admin_user', :'reason', now());

commit;
```

Then re-derive the order's status by hand — the voucher count changed, and nothing in this
transaction recomputes it:

```sql
-- Delivered vs wanted. If the order is still owed fuel it belongs in PendingFulfillment, and the
-- next sweep will try to fill it again; if it is not owed anything, it is Fulfilled.
select li.quantity,
       count(f.id) filter (where f.released_at_utc is null) as delivered
from order_line_items li
left join fulfillments f on f.line_item_id = li.id
where li.order_id = :'order'
group by li.id, li.quantity;
```

```sql
-- Still owed fuel: back to the queue, and the next sweep will try again.
update orders
set status = 'PendingFulfillment', fulfilled_at_utc = null,
    partially_fulfilled_since_utc = null, updated_at_utc = now()
where id = :'order';

-- Nothing owed any more.
update orders
set status = 'Fulfilled', fulfilled_at_utc = now(), updated_at_utc = now()
where id = :'order';
```

Run the one that matches the comparison above — not both.

Finally, prove the three tables agree:

```sql
select v.status, v.order_id, v.assigned_to_user_id,
       (select count(*) from fulfillments f
         where f.voucher_id = v.id and f.released_at_utc is null) as live_deliveries,
       (select count(*) from voucher_movements m
         where m.voucher_id = v.id and m.kind = 'returned') as returned_movements,
       (select count(*) from voucher_events e
         where e.voucher_id = v.id and e.to_status = 'Available') as back_to_pool_events
from fuel_vouchers v
where v.id = :'voucher';
```

Expected: `status = Available`, `order_id` and `assigned_to_user_id` null, `live_deliveries = 0`,
and the movement count equal to the event count. A voucher is not "in stock" because one column
says so; it is in stock when all three tell the same story.

## PendingReconciliation vouchers are not stock

A `PendingReconciliation` voucher is a **claim**, not a free ticket: the holder reported using it at
a station and the supplier's statement has not confirmed it yet. It is unredeemable by the app,
excluded from the pool, and it leaves that state only through the redemption queue — approved
(`Used`), rejected or withdrawn (`Assigned`). Never count it as stock, and never release it to
another order. The queue and its daily procedure are in
`docs/SUPPLIER_REDEMPTION_RECONCILIATION_RUNBOOK.md`.
