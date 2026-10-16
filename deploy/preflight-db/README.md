# `preflight-db/` — data preconditions, checked before anything is pulled

Each `.sql` file here is a **read-only assertion about production data**. The runner executes
them all and reports anything that comes back.

## The contract

1. **One row per violated precondition. Zero rows means the data is fine.** The runner does not
   parse the output; it counts rows.
2. **Read-only, enforced.** Every file opens with `SET default_transaction_read_only = on;`, so a
   statement that tries to modify anything fails. This is not a convention — a preflight on the
   deploy path that *could* write to production would be a far worse addition than the outage it
   is here to prevent.
3. **Name the thing that is wrong in the output.** A row should be enough to act on without a
   second query. The violation has to be identifiable from the output alone, because the person
   reading it is usually mid-deploy.
4. **Never raise. Report.** An assertion that aborts is a migration's job; an assertion that
   reports is a preflight's.

## Why this directory exists

On 2026-10-15 `SplitFuelPackagesIntoOffers` shipped with a guard that refused to collapse volume
rows whose per-litre prices disagreed — correctly, because it cannot choose which copy is true.
But the guard ran inside the migration, which runs at container start, which means it took the
production API down before anyone saw the message:

```
fuel_packages carries more than one per-litre price for:
  okko/24dde716-e638-4f64-96d0-f43fb083c5f7/real,
  okko/fa63b7ab-2fb2-4cdc-bc71-ee1aff9a3785/real
```

Staging could not catch it: staging's `fuel_packages` holds no imported fuel types, so the drift
simply is not there to be found. The same condition asked here, before the pull, is a thirty-second
answer instead of an outage.

**Both places kept the guard.** A precondition passing is not a promise, and the migration had to
still refuse rather than pick a copy if the data changed between the preflight and the boot.

## What `010` asks now

The file was renamed and rewritten when `DropFuelPackages` removed the table the original detector
read. The question changed shape, not purpose.

The old detector counted how many distinct values a `(station, fuel, pool)` group held for each
per-litre column on `fuel_packages`. Per-litre pricing then moved onto `fuel_offers`, and the unique
index on `(station_id, fuel_type_id, data_kind)` makes one group exactly one row — so that
disagreement is no longer representable and there would be nothing to count.

What is still expressible is the pair that now holds a price in two places: an offer names a rate, a
volume names what that nominal costs, and nothing ties them together. So `010` now reports every live
volume whose price disagrees with its offer by more than half a kopeck — **in UAH**, as
`discrepancy_uah`, because that is what a customer would be over- or under-charged and it is the
number a human resolving it needs. Offers with no `final_price_per_liter` are skipped, not reported:
there is no rate to disagree with, and firing on the legacy cost-plus rows the pricing migration
deliberately left alone would put a permanent warning on the deploy path.

The tests are `OfferVolumePriceAgreementTests`, and they read this file from disk rather than
repeating its SQL — so the two cannot drift apart.

## Adding a file

- Name it `NNN-what-it-checks.sql`, numbered so output reads in a fixed order.
- Do not write to a table, and do not `DO $$ ... RAISE`.
- State in the header what the previous failure looked like, so the check earns its place by
  referring to a real outage rather than a hypothetical.

## Wiring

`preflight.sh` step 5 runs these. It **warns** rather than fails by default, because production
currently violates `010` and a blocking check would stop every deploy behind it. Once
[planning #328](https://github.com/art-of-v/fuel-voucher-platform-planning/issues/328) reconciles
the two OKKO fuel types, the check is switched to blocking by setting
`PREFLIGHT_DB_STRICT=1` — that is the whole flip, and it is deliberately a separate decision from
adding the check.

Running one by hand:

```bash
docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -X -f - < deploy/preflight-db/010-offer-volume-price-agreement.sql
```