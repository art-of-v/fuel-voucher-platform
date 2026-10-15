-- Data preconditions, checked BEFORE a new image is pulled.
--
-- One row per (station, fuel, pool) group that carries more than one value for a
-- per-litre pricing column. Zero rows means the catalogue agrees with itself.
--
-- WHY THIS EXISTS. `SplitFuelPackagesIntoOffers` collapsed `fuel_packages` into one
-- `fuel_offers` row per (station, fuel, pool), and its guard refused to apply on
-- production on 2026-10-15:
--
--     fuel_packages carries more than one per-litre price for:
--       okko/24dde716-e638-4f64-96d0-f43fb083c5f7/real,
--       okko/fa63b7ab-2fb2-4cdc-bc71-ee1aff9a3785/real
--
-- The migration is the right place to refuse - it cannot silently pick a copy - but a
-- refusal there is a FAILED DEPLOY and a downed site, because the migration runs inside
-- the container that is already replacing the live one. That is the wrong shape for a
-- condition a human has to resolve. This file is the same assertion, asked earlier,
-- read-only, where the answer costs thirty seconds and nothing else.
--
-- Read-only is enforced, not requested: `default_transaction_read_only` below makes
-- every statement in the session fail to modify anything. A preflight that could write
-- to production would be a much worse thing to add to the deploy path.
--
-- ARCHIVED ROWS ARE EXCLUDED, deliberately. An archived volume is never read by the
-- storefront, so a stale price on one cannot publish anything. A blocking check that
-- fires on historical rows trains people to ignore it, and a gate nobody reads is not a
-- gate. Historical drift is reported by the reconciliation, not by this file.

-- Read-only is enforced by an explicit transaction, not by
-- `SET default_transaction_read_only`. That setting does bind every transaction started
-- later in the same session — but psql runs each statement in autocommit, so a write
-- appended after this query would be its own transaction and would sail straight past it.
-- A guarantee that turns out to be decorative on a production deploy path is worse than
-- none, because it is believed. Wrapping the query in BEGIN / SET TRANSACTION READ ONLY
-- binds the setting to the transaction that actually runs it, and Postgres refuses a
-- write inside one. FuelPackagePriceDriftTests asserts that refusal rather than trusting
-- this comment.

BEGIN;
SET TRANSACTION READ ONLY;

WITH per_group AS (
    SELECT
        station_id,
        fuel_type_id,
        data_kind,
        count(*)                                                   AS volumes,
        -- COALESCE(col::text, '<null>') and NOT count(DISTINCT col): the aggregate
        -- skips NULLs, so a group holding 50 and NULL counts as ONE distinct value and
        -- reads as agreement. That is exactly the "one volume carries no price" case,
        -- and it is the one a plain DISTINCT would miss.
        count(DISTINCT COALESCE(final_price_per_liter::text, '<null>'))    AS d_final,
        count(DISTINCT COALESCE(pump_price_per_liter::text, '<null>'))     AS d_pump,
        count(DISTINCT COALESCE(min_discount_per_liter::text, '<null>'))   AS d_min_discount,
        count(DISTINCT COALESCE(supplier_price_per_liter::text, '<null>')) AS d_supplier,
        count(DISTINCT COALESCE(margin_uah_per_liter::text, '<null>'))     AS d_margin,
        count(DISTINCT COALESCE(margin_percent::text, '<null>'))           AS d_margin_percent
    FROM fuel_packages
    WHERE NOT is_deleted
    GROUP BY station_id, fuel_type_id, data_kind
)
SELECT
    g.station_id,
    g.fuel_type_id,
    g.data_kind,
    g.volumes,
    d.column_name,
    d.distinct_values
FROM per_group g
CROSS JOIN LATERAL (VALUES
    ('final_price_per_liter',    g.d_final),
    ('pump_price_per_liter',     g.d_pump),
    ('min_discount_per_liter',   g.d_min_discount),
    ('supplier_price_per_liter', g.d_supplier),
    ('margin_uah_per_liter',     g.d_margin),
    ('margin_percent',           g.d_margin_percent)
) AS d(column_name, distinct_values)
WHERE d.distinct_values > 1
ORDER BY g.station_id, g.fuel_type_id, g.data_kind, d.column_name;

COMMIT;