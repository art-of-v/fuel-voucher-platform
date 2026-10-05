# Renewal and short-term-sale emulator fixtures

Operational SQL for testing the two-date voucher model on a live database. Three files, run in order,
each idempotent:

| File | What it does |
|---|---|
| `00-audit-expired.sql` | Reports what would be deleted and what is being kept. **Changes nothing** unless invoked with `-v apply=1`. |
| `01-seed-emulators.sql` | Creates one voucher per branch of the model, the stock that must and must not match, and the purchase catalog. |
| `99-cleanup-emulators.sql` | Removes exactly what the fixtures created. Cannot touch a customer's vouchers. |

Run from the app host:

```bash
docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 < 00-audit-expired.sql
# read the output before deleting anything
docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 -v apply=1 < 00-audit-expired.sql
docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 < 01-seed-emulators.sql
```

`01` can be re-run at any time: it drops the previous `EMU2-` set first and recreates it.

## What is protected, and how

The customer's own vouchers are never touched. They are resolved by **phone suffix (`%1771`)**, not by
a hardcoded id, because ids change when the test account is recreated. Protected means: assigned to
that customer, or belonging to a company that customer works for. On top of that, a voucher is kept if
anything references it — a fulfilment, a renewal item (as source or as result), a supplier exchange, an
operator renewal, or a paid order.

That last group is why the audit exists as a separate step. `fuel_vouchers` is referenced with
`ON DELETE RESTRICT` from four tables, so a naive delete fails halfway and leaves a mess. The script
unhooks dependents first, in one transaction, and prints what it kept alongside the reason.

The date filter is `customer_expiration_date < CURRENT_DATE` — **not** `status = 'Expired'`. A voucher
left as `Available` with a lapsed date is precisely the row that makes the stock count lie, so filtering
on status would leave exactly the wrong rows behind.

## The fixture matrix

Sources are what the customer holds and can renew. `room` is how much the supplier's real term still
adds on top of what the customer has.

| `voucher_number` | customer term | provider term | room | Branch it must take |
|---|---|---|---|---|
| `EMU2-SRC-EXT-1M` | +10d | +100d | 90d | **Extend** 1m, same voucher |
| `EMU2-SRC-EXT-EXACT` | +10d | +40d | 30d | **Extend** 1m landing exactly on the ceiling |
| `EMU2-SRC-CEIL-NOROOM` | +10d | +13d | 3d | **Replace** — no tier fits; the case that stalled in #815 |
| `EMU2-SRC-LAPSED-CUST` | −3d | +90d | 93d | **Replace** (customer term gone, supplier term fine) |
| `EMU2-SRC-DEAD-BOTH` | −30d | −1d | — | **Replace**, needs stock dated ≥ today + term |
| `EMU2-SRC-OUTSIDE-WINDOW` | +60d | +200d | 140d | **Not renewable at all** — past the 14-day trigger |
| `EMU2-SRC-BLOCKED` | +10d | +100d | 90d | **Refused** — status guard only; dates are extendable |
| `EMU2-SRC-USED` | +10d | +100d | 90d | **Refused** — status guard only |
| `EMU2-SRC-COMPANY-REPLACE` | −2d | +90d | 92d | **Replace**, replacement must inherit the company |

Stock is what a replacement may be served from. The selector requires provider + fuel + litres to match
exactly and the paper term to reach `today + chosen term`, so the rows that matter most are the ones
that must **not** match.

| `voucher_number` | Match | Purpose |
|---|---|---|
| `EMU2-STK-LONG-6M` | serves every tier | the general-purpose replacement |
| `EMU2-STK-MID-3M` | serves up to 3m | proves the tier is not silently widened |
| `EMU2-STK-EXACT-1M` | serves 1m only, 3m unavailable | boundary: exactly enough |
| `EMU2-STK-SHORT-20D` | serves 1w/2w, **not** 1m | proves the tier availability follows real dates |
| `EMU2-STK-STALE-5D` | serves nothing | the "in stock by status, useless by date" trap |
| `EMU2-STK-OTHER-20L` | must never match | different nominal |
| `EMU2-STK-OTHER-FUEL` | must never match | different fuel |
| `EMU2-STK-OTHER-PROV` | must never match | different provider |
| `EMU2-STK-BATCH-1..3` | three interchangeable | the batch scenario needs a **distinct** voucher per line |
| `EMU2-STK-COMPANY` | matches | company-owned stock |

Purchase side (`emu2-pkg-*`): 10 L and 20 L packages pinned to a known cost so the money probes are
meaningful — supplier cost 40 ₴/L, margin 20 ₴/L, shelf 60 ₴/L. A 20 ₴/L term discount lands exactly on
cost (sellable, zero margin); 25 ₴/L goes under it and must be refused. The seed **aborts** if the fuel
has `allow_below_cost = true`, because that opt-in bypasses the guard and would make the refusal test
pass for the wrong reason.

## Schema facts this had to learn the hard way

Found by running it, not by reading the entities. Each one costs a failed statement:

- **uel_vouchers.status is a varchar, not the enum's ordinal.** Values are 'Available',
  'Assigned', 'Used', 'Blocked', 'Expired'. Inserting 4 fails on the type.
- **uel_types.id is a uuid** (a63b7ab-… is ДП ЄВРО on station okko), not a natural code
  like okko-95. The fixtures pin that one fuel and clone the packages from it.
- **CHECK ck_voucher_held_has_order**: a voucher in Assigned, Used or Blocked must have an
  order_id. Renewal only accepts those statuses, so a customer-held fixture cannot exist without a
  purchase behind it — hence one Fulfilled order plus a line item per source voucher.
- **Never UPDATE fuel_vouchers SET order_id = NULL as a cleanup step.** The check is evaluated per
  row on UPDATE, so it fires on the way out and aborts the transaction before the DELETE that would
  have made it moot. Delete the vouchers, then the orders.
- **A literal % inside RAISE EXCEPTION** is a format placeholder, and the statement dies with
  	oo few parameters specified for RAISE. Escape it or reword.
- **NULL needs ::uuid in a UNION ALL branch**, or Postgres resolves the shared column as text
  and the insert fails with ssigned_to_user_id is of type uuid but expression is of type text.

## What the fixtures deliberately do not do

They do not configure the purchase ladder (`VoucherTerm:*`). T0 has to run against a database where the
feature has never been switched on — that is the state a configuration mistake leaves behind, and it is
the state worth proving. Switch it on in admin → Settings → "Short-term fuel selling" only after T0
passes.

They also create no orders. Every fixture is a voucher or a catalog row; orders come from real Monobank
sandbox payments so the invoice, webhook and fulfilment chain runs for real.