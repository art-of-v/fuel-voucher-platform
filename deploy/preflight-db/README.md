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

| File | Asks | In strict mode |
|---|---|---|
| `011-fuel-type-name-twins.sql` | Does any brand have more than one **live** fuel row under one name | blocks the deploy |
| `013-fuel-type-references-agree-on-type.sql` | Does any column that names a fuel type disagree with `fuel_types.id` about what a fuel type's id IS | blocks the deploy |
| `014-fuel-type-references-are-all-uuids.sql` | Does any reference to a fuel type still hold a TEXT-shaped id rather than a uuid | blocks the deploy |
| `015-roles-columns-match-the-model.sql` | Are the columns of `roles` exactly the set the EF model maps | blocks the deploy |
| `016-migration-ids-are-well-formed.sql` | Is every `MigrationId` `^<14 digits>_<name>$`, and recorded once | blocks the deploy |
| `017-fuels-without-a-supplier-cost.sql` | Which fuels **that are in stock** have no supplier cost, or a cost and no margin | blocks the deploy |
| `018-fuel-type-grade-twins.sql` | Do two **live** fuels at one brand spell one grade two ways | blocks the deploy |

**Every file in this directory is the same instrument**: a warning by default, a failed
deploy when `PREFLIGHT_DB_STRICT` is on. The distinction between the files is what they
ask, not how hard they push - a violated precondition is a line in the deploy log either
way, and the mode decides whether the deploy stops.

`011` is the one its own migration refuses on: a failed deploy with the refusal on the box, versus
thirty seconds and a list of rows to reconcile. One live fuel per name per brand is what makes the name
identify a fuel, which is what every other key in the schema depends on. It groups by `brand_id`,
because `20261018191320_CatalogueBrandsBecomeUuid` renamed `fuel_types.station_id` to `brand_id` -
a station was a brand - and the index it guards is `ux_fuel_types_brand_name`.

`012-offer-station-matches-its-fuel-type.sql` was deleted on 2026-10-19, together with the table it asked
about. It asked whether `fuel_offers.brand_id` disagreed with `fuel_types.brand_id`, to justify an offer key
that carried no station in it. There is no offer row any more and the key is the fuel type's own id, so
the question cannot be asked and there is nothing left for an answer to worry about. Its number is not
reused, the way `010`'s is not.

`013` and `014` were added on 2026-10-20 with planning D-21's second slice, which turned the fuel type's
id from a text key into a uuid. `013` asks whether the columns that reference a fuel type agree with
`fuel_types.id` about its TYPE; `014` asks whether any of them still holds a text-shaped id. They are two
questions about the same change, asked two ways, because a type change and a value change fail differently.
`015` and `016` came out of the production review of 2026-10-19: production carried two dead columns on
`roles` and one space-prefixed row in the migration history, and neither the CI gate nor the model could
see either. `017` names the fuels whose below-cost guard is switched off because the inputs are missing -
and it is the file that makes the catalogue's missing cost visible on every deploy.

**`017` asks about the fuel that is IN STOCK, and about nothing else.** The catalogue is not the
warehouse: a fuel with a price in the columns and no vouchers behind it cannot be sold, so nothing about
its cost can cost us money, and a guard that fires on it is a guard that fires on nothing. This is the
rule the application already follows - `CreateCheckoutCommandHandler` checks the stock and throws
`PackageOutOfStockException` BEFORE the below-cost verdict is asked, and `UnknownCostSaleBlockedException`
says in its own words that a fuel *that is in stock* and has no recorded cost cannot be sold.

The difference was not academic. Measured on production 2026-10-21: the catalogue holds 23 fuels and the
warehouse holds **zero** vouchers, so all 23 were a false positive - 46 reported rows naming fuel that
cannot be sold. With `PREFLIGHT_DB_STRICT=1` that blocked every deploy, and it did so on a table with
nothing to say: five consecutive production deploys failed on this file alone, from 07:43 to 09:20, while
production served 200s throughout. The file now joins the stock the same way the storefront counts it
(`PackageStockAvailability.SellableKeys`: `Available`, in the `real` pool, not deleted, not past its
supplier expiry), so a gate that used a different definition than the shop would report fuels the shop
never offers - the same class of bug as the one the file had.

**`018` asks whether one grade is written two ways at one brand.** The catalogue says a grade once:
"А-95" and "A-95" are one grade, and the owner's decision (planning #349, 2026-10-21) is that the
second spelling must not become a second row. `011` asks whether one NAME is written twice; `018`
asks the harder question - the same grade under two spellings - and the index it guards is
`ux_fuel_types_brand_grade_key`, on (brand, the name folded onto one spelling).

The rule is written out in the file rather than called, because the function that computes the key
(`fuelflow_fuel_grade_key`) arrives with the migration that adds the column: on the deploy that
carries the migration there is nothing to call, and the migration's own guard is what refuses
there. `FuelTypeGradeKeyTests` reads both and compares them over every name the catalogue holds,
and seeds a twin to prove the file finds one.


## A check that cannot run is a warning, never a pass

The runner used to count the rows psql printed and call the file `ok` when the count was zero. A
query PostgreSQL **refused** also prints zero rows - the error goes to stderr, which was sent to
`/dev/null` - and psql exits 0 on a SQL error unless it is told otherwise. So `011` spent a week
grouping by a column that had been renamed, aborting on every deploy, and printing
`011-fuel-type-name-twins.sql: ok` every time. A gate that cannot run was a gate that said yes.

Three things prevent that now:

- the runner passes `ON_ERROR_STOP=1` and reads psql's exit code, so a refused query is an exit
  code rather than a silent empty result;
- a file that could not run is reported as **could not run**, with psql's own error line, and is
  counted separately from the files that ran - so "all data preconditions that ran are satisfied"
  cannot be printed over a check that never ran;
- `PreflightSqlExecutesTests` executes every file in this directory against a database built by the
  same migrations the application runs, so a renamed column fails the build instead of the deploy.

`deploy/preflight-db.test.sh` covers the runner's side of it: a stub whose psql fails must produce
a warning naming the file, and never the word `ok`.

## Adding a file

- Name it `NNN-what-it-checks.sql`, numbered so output reads in a fixed order.
- Do not write to a table, and do not `DO $$ ... RAISE`.
- State in the header what the previous failure looked like, so the check earns its place by
  referring to a real outage rather than a hypothetical.
- Wrap it in `BEGIN; / SET TRANSACTION READ ONLY; / COMMIT;`, and add a row to `PreflightDbFileTests`
  — that test walks the directory and fails if a file ships without a case, so a new check cannot
  arrive without the read-only guarantee the whole directory runs on.
- It has to **run**: `PreflightSqlExecutesTests` executes every file in this directory against the
  migrated schema, so a column that no longer exists fails the build rather than the deploy.

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