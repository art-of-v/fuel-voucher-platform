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

## `010` is gone, and that is the point

`010-offer-volume-price-agreement.sql` was deleted on 2026-10-17, together with its tests
(`OfferVolumePriceAgreementTests`). It existed to answer one question — *does a volume's stored total
disagree with its offer's rate, and by how much* — and there is no longer a stored total to disagree.

The chain that made it necessary, in order:

1. `SplitFuelPackagesIntoOffers` put the per-litre rate on `fuel_offers` and left each volume holding its
   own total. One price, two copies, nothing tying them together: `010` was written for that.
2. `DeriveTheOfferFinalPrice` (2026-10-17) dropped `fuel_offers.final_price_per_liter`, so the rate is
   derived from the offer's knobs and the offer has a single source of truth for it. `010` was rewritten to
   derive its rate too, and kept asking the same question.
3. `PackagePriceOverrideOnNominals` (2026-10-17) replaced `fuel_offer_volumes.package_price` with a nullable
   `price_override_uah`. Null means "the offer's rate x litres"; a number means an operator priced that size
   on purpose. There was no longer a copy of the offer's price anywhere - `FuelPricing.PackagePrice` computed
   the effective one, and every reader (shelf, quote, charge) used that same function, so a drift between
   what is shown and what is charged was not representable.
4. `NoOneOffPrices` (2026-10-18) dropped `price_override_uah` and its non-negative check. A per-nominal price
   set by hand was the last way to say something other than "the formula decided", and it was a second source
   of truth wearing the first one's clothes: a repricing moved every nominal but that one, so a price shown on
   the shelf could silently differ from the price charged. What replaced it is nothing - every nominal is now
   priced from its fuel's rate, and the margin lives on the fuel. That is also why the rehearsal and live
   pools of one fuel are priced alike: the pool separates stock and data, not money.

Deleting the check is therefore the intended outcome, not a gap. If a future change introduces a second
copy of a price, it needs a check like this again — and `010`'s header, which is in the git history, says
what to write.

## The files

| File | Asks | Blocks |
|---|---|---|
| `011-fuel-type-name-twins.sql` | Does any station have more than one **live** fuel row under one name | yes |
| `012-offer-station-matches-its-fuel-type.sql` | Does any offer name a station its own fuel type does not | no — a question, not a gate (see its header) |

`011` is a precondition its migration refuses on: a failed deploy with the refusal on the box, versus
thirty seconds and a list of rows to reconcile. `012` is different — it cannot be a precondition, because no
constraint can span two tables, and it guards a state that is legal until proved otherwise. Both files exist
because `DropTheStationPrefixFromOfferKeys` (2026-10-17) keys an offer by its fuel type id alone: one live
fuel per name per station is what makes the name identify a fuel, and an offer's station has to be its fuel
type's for that key to mean anything.

## Adding a file

- Name it `NNN-what-it-checks.sql`, numbered so output reads in a fixed order.
- Do not write to a table, and do not `DO $$ ... RAISE`.
- State in the header what the previous failure looked like, so the check earns its place by
  referring to a real outage rather than a hypothetical.
- Wrap it in `BEGIN; / SET TRANSACTION READ ONLY; / COMMIT;`, and add a row to `PreflightDbFileTests`
  — that test walks the directory and fails if a file ships without a case, so a new check cannot
  arrive without the read-only guarantee the whole directory runs on.

## Wiring

`preflight.sh` step 5 runs these. It **warns** rather than fails by default.

**That default was a temporary condition, and it no longer holds.** The check used to warn because
production *did* violate it: two OKKO fuel types carried different per-litre prices across their
volumes, and a blocking check would have stopped every deploy behind them. That was
[planning #328](https://github.com/art-of-v/fuel-voucher-platform-planning/issues/328), and it is
resolved — the check that caught it (`010`) has since been deleted along with the drift it watched for.
The remaining gate, `011`, is strict and its data was clean when it was written:

```bash
docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -X -f - < deploy/preflight-db/011-fuel-type-name-twins.sql
```

So strict mode is available and is enabled.

**Where `PREFLIGHT_DB_STRICT` has to be set, which is the part that is easy to get wrong.** It is
read from the environment of the `preflight.sh` **process**. The script passes `--env-file` to
`docker compose` and never sources anything into its own shell, so writing it into `deploy/.env` has
no effect — the line would still read the `:-0` default. The CI job that runs the preflight sets it
from the repository variable `PREFLIGHT_DB_STRICT`, so flipping it is a settings change rather than
an edit to a file on the box. It stays a separate decision from adding the check: a warning that
blocks nothing and a gate that stops a deploy are different instruments, and only one of them is
worth having if the data is actually clean.

Running one by hand:

```bash
docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -X -f - < deploy/preflight-db/011-fuel-type-name-twins.sql
```