-- Data preconditions, checked BEFORE a new image is pulled.
--
-- Every fuel that is IN STOCK and has no recorded supplier cost, and every such fuel that has a
-- cost but no margin. Zero rows means the below-cost guard can do its job on every fuel that
-- can actually be sold.
--
-- WHY THIS EXISTS. The guard is real and it is checked at checkout, but it is SKIPPED when the
-- inputs are missing, and that is deliberate:
--
--     if (!fuelType.AllowBelowCost
--         && package.SupplierPricePerLiter is { } cost      <- NULL: the whole block is skipped
--         && package.MarginUahPerLiter is { } profit
--         && FuelPricing.IsBelowCost(cost, profit, package.PumpPricePerLiter, ...))
--
-- and `IsBelowCost` opens with `costPerLiter > 0m &&`, so a fuel with no cost is never below
-- cost. The comment on it says why: "a fuel with no cost (0) is never below cost, so legacy /
-- uncosted fuels are never blocked" - which is the right behaviour for a legacy import, and
-- the wrong behaviour for a catalogue where the fuel being sold is unpriced.
--
-- ONLY FUEL THAT IS IN STOCK COUNTS. This is the rule the whole file is built on, and it is the
-- rule the code already follows:
--
--   * `allow_below_cost` is the switch that says "this fuel may be sold below cost". It is
--     consulted where a SALE happens, and a sale needs stock: `CreateCheckoutCommandHandler`
--     checks `IsInStockAsync` and throws `PackageOutOfStockException` BEFORE the below-cost
--     verdict is asked, and `UnknownCostSaleBlockedException` says in its own words - "A fuel
--     that is IN STOCK and has no recorded cost cannot be sold".
--   * A fuel with no stock cannot be sold at all, so nothing about its cost can cost us money.
--     It has a price in the columns and no vouchers behind it - that is a catalogue entry, not
--     a product, and a guard that fires on it is a guard that fires on nothing.
--
-- This file used to ask about EVERY fuel in `fuel_types`, and the difference was not academic.
-- Measured on production 2026-10-21: the catalogue holds 23 fuels and the warehouse holds ZERO
-- vouchers (`SELECT count(*) FROM fuel_vouchers` -> 0), so all 23 rows were a false positive -
-- 46 reported rows naming fuel that cannot be sold. With `PREFLIGHT_DB_STRICT=1` that blocked
-- every deploy, and it did so on a table with nothing to say: five consecutive production
-- deploys failed on this file alone, from 07:43 to 09:20, while production served 200s
-- throughout.
--
-- So the file asks the question that has a consequence: of the fuel that is in stock, which has
-- no cost and no margin? A fuel that IS in stock and IS unpriced is a real problem - the
-- storefront lists it, and checkout refuses it - and that is the fuel this file names.
--
-- WHAT "IN STOCK" MEANS HERE, and why it is written out rather than approximated. The definition
-- is the one the application itself uses, `PackageStockAvailability.SellableKeys`: a voucher that
-- is `Available`, in the `real` pool, not deleted, and not past its supplier expiry. The
-- storefront advertises a package as buyable on exactly that predicate and the checkout charges
-- for it on exactly that predicate, so a gate that used a different one would report fuels the
-- shop never offers - which is the same class of bug as the one this file had.
--
-- The `qa` pool is excluded on purpose: rehearsal data is not sold to a customer, so a fuel that
-- exists only for rehearsal cannot lose us money either. It is also the pool a `data_kind`
-- mistake would put live rows in, and this gate must not be the thing that reports a rehearsal
-- row as a production problem.
--
-- The expiry is `supplier_expiration_date`, the paper term the supplier sold us: stock past it
-- is not sellable, and the storefront hides it for the same reason.
--
-- THE ID IS CAST TO TEXT because `fuel_types.id` is a uuid since 2026-10-20
-- (`20261020084928_FuelTypeIdsBecomeUuid`) and `uuid || text` is not an operator Postgres has:
-- the file would stop running the day the column changed, and a precondition that cannot run
-- reports success without having checked anything. The cast runs on both shapes.

BEGIN;
SET TRANSACTION READ ONLY;

SELECT to_regclass('public.fuel_types') IS NOT NULL AS t \gset
\if :t
-- The fuel that is in stock: an Available, unexpired, undeleted voucher in the real pool.
-- `EXISTS` rather than a join, because the question is about the FUEL and one fuel can have
-- many vouchers - a join would report the same fuel once per voucher behind it.
WITH in_stock AS (
    SELECT DISTINCT v.fuel_type_id
    FROM fuel_vouchers v
    WHERE v.status = 'Available'
      AND v.data_kind = 'real'
      AND NOT v.is_deleted
      AND v.supplier_expiration_date >= CURRENT_DATE
)
SELECT 'fuel_types' AS table_name,
       'no supplier cost - the below-cost guard is skipped for this fuel' AS violation,
       f.name || ' (brand ' || b.slug || ', id ' || f.id::text || ')' AS detail
FROM fuel_types f
LEFT JOIN brands b ON b.id = f.brand_id
JOIN in_stock s ON s.fuel_type_id = f.id
WHERE NOT f.is_deleted
  AND (f.supplier_price_per_liter IS NULL OR f.supplier_price_per_liter <= 0)
  AND NOT f.allow_below_cost
UNION ALL
SELECT 'fuel_types' AS table_name,
       'no margin - the below-cost guard is skipped for this fuel' AS violation,
       f.name || ' (brand ' || b.slug || ', id ' || f.id::text || ')' AS detail
FROM fuel_types f
LEFT JOIN brands b ON b.id = f.brand_id
JOIN in_stock s ON s.fuel_type_id = f.id
WHERE NOT f.is_deleted
  AND (f.margin_uah_per_liter IS NULL)
  AND NOT f.allow_below_cost
ORDER BY 3;
\endif

COMMIT;
