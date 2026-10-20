-- Data preconditions, checked BEFORE a new image is pulled.
--
-- Every fuel that has no recorded supplier cost, and every fuel that has a cost but no margin.
-- Zero rows means the below-cost guard can do its job on every fuel in the catalogue.
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
-- the wrong behaviour for a catalogue where NOTHING is costed.
--
-- Measured on production 2026-10-20: all 23 fuels have `supplier_price_per_liter` NULL and all
-- 23 have `margin_uah_per_liter` NULL, and `allow_below_cost` is false on every one of them.
-- So the business has NOT opted any fuel in as a deliberate loss-leader, and the guard that
-- would have stopped a below-cost sale is inert for the entire catalogue - silently, because
-- the block is skipped rather than refused.
--
-- This is a QUESTION, not a gate. The numbers are the business's: the admin can enter them
-- (admin/src/components/BrandsTab.tsx -> the supplier cost field; the API writes
-- fuel_types.supplier_price_per_liter and fuel_types.margin_uah_per_liter), and nothing here
-- blocks a deploy over them. What this file does is make the number impossible to forget: it
-- is in the deploy log, every deploy, naming the fuels.
--
-- If a fuel legitimately has no cost, `allow_below_cost` is the switch that says so - and it
-- is false on all 23, so today nobody has said so about any of them.
--
-- THE ID IS CAST TO TEXT because `fuel_types.id` is a uuid since 2026-10-20
-- (`20261020084928_FuelTypeIdsBecomeUuid`) and `uuid || text` is not an operator Postgres has:
-- the file would stop running the day the column changed, and a precondition that cannot run
-- reports success without having checked anything. The cast runs on both shapes.

BEGIN;
SET TRANSACTION READ ONLY;

SELECT to_regclass('public.fuel_types') IS NOT NULL AS t \gset
\if :t
SELECT 'fuel_types' AS table_name,
       'no supplier cost - the below-cost guard is skipped for this fuel' AS violation,
       f.name || ' (brand ' || b.slug || ', id ' || f.id::text || ')' AS detail
FROM fuel_types f
LEFT JOIN brands b ON b.id = f.brand_id
WHERE NOT f.is_deleted
  AND (f.supplier_price_per_liter IS NULL OR f.supplier_price_per_liter <= 0)
  AND NOT f.allow_below_cost
UNION ALL
SELECT 'fuel_types' AS table_name,
       'no margin - the below-cost guard is skipped for this fuel' AS violation,
       f.name || ' (brand ' || b.slug || ', id ' || f.id::text || ')' AS detail
FROM fuel_types f
LEFT JOIN brands b ON b.id = f.brand_id
WHERE NOT f.is_deleted
  AND (f.margin_uah_per_liter IS NULL)
  AND NOT f.allow_below_cost
ORDER BY 3;
\endif

COMMIT;
