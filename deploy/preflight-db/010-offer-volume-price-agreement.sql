-- Data preconditions, checked BEFORE a new image is pulled.
--
-- One row per live volume whose package price disagrees with the per-litre rate of the offer it
-- belongs to, by more than half a kopeck. Zero rows means the catalogue agrees with itself.
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
-- WHAT CHANGED, AND WHY THE QUESTION CHANGED. Twice now.
--
-- 1. This used to count how many distinct values a (station, fuel, pool) group held for each
--    per-litre column on `fuel_packages`. That question became unaskable when per-litre pricing
--    moved onto `fuel_offers`, because the unique index on (station_id, fuel_type_id, data_kind)
--    means one group is one row: the disagreement those columns could hold cannot be represented.
--
-- 2. What CAN still disagree is the pair that carries the price in two places - an offer names
--    a rate per litre, a volume names what that nominal costs in total - and it is still the
--    only remaining copy. The offer's stored `final_price_per_liter` is gone as of
--    `20261017150207_DeriveTheOfferFinalPrice`: it was `min(cost + margin, pump - minDiscount)`
--    stored beside the columns it is computed from, and every write path computed it that way
--    before writing it. So the rate below is DERIVED, and the expression mirrors
--    `FuelPricing.FinalPerLiterOrNull` including its nullability: no cost or no margin means
--    "not priced", which is skipped rather than treated as zero.
--
-- The row still exists because the drift is money: a volume that disagrees with its offer
-- advertises one number and charges another, and `discrepancy_uah` is what the customer would
-- be over- or under-charged, not a ratio.
--
-- OFFERS WITH NO COST OR NO MARGIN ARE SKIPPED, not reported. Such an offer has no rate to
-- disagree with - on production that is a fuel the catalogue import created and nobody has
-- priced yet, and it has no volumes either. Reporting them would fire the gate on rows the
-- system deliberately permits, and a gate that cries wolf on the permitted state is a gate
-- people learn to click past.
--
-- ARCHIVED ROWS ARE EXCLUDED, deliberately. An archived volume is never read by the storefront,
-- so a stale price on one cannot publish anything. A blocking check that fires on historical rows
-- trains people to ignore it, and a gate nobody reads is not a gate. Historical drift is reported
-- by the reconciliation, not by this file.
--
-- Read-only is enforced by an explicit transaction, not by
-- `SET default_transaction_read_only`. That setting does bind every transaction started
-- later in the same session — but psql runs each statement in autocommit, so a write
-- appended after this query would be its own transaction and would sail straight past it.
-- A guarantee that turns out to be decorative on a production deploy path is worse than
-- none, because it is believed. Wrapping the query in BEGIN / SET TRANSACTION READ ONLY
-- binds the setting to the transaction that actually runs it, and Postgres refuses a
-- write inside one. `OfferVolumePriceAgreementTests` asserts that refusal rather than
-- trusting this comment.
--
-- The query is a flat `SELECT ... FROM ( ... ) AS rated`, not a `WITH` clause, and that is not a style
-- preference. `OfferVolumePriceAgreementTests` reads this file from disk and slices the query out of it,
-- from the first line-starting `SELECT` to the first semicolon after it, because that is what the
-- deploy's `psql -t -A -F'|'` sees as one statement. A leading `WITH` would be sliced off and its inner
-- relation unresolvable, and a semicolon inside the query — even inside a `--` comment — would cut the
-- statement short. Both cost a failed deploy, so the shape is stated here rather than rediscovered.

BEGIN;
SET TRANSACTION READ ONLY;

SELECT
    rated.offer_id,
    rated.station_id,
    rated.fuel_type_id,
    rated.data_kind,
    rated.liters,
    rated.rate_per_liter,
    rated.package_price,
    round(rated.rate_per_liter * rated.liters, 2)                  AS price_from_rate,
    round(rated.package_price - rated.rate_per_liter * rated.liters, 2) AS discrepancy_uah
FROM (
    SELECT
        o.id                AS offer_id,
        o.station_id,
        o.fuel_type_id,
        o.data_kind,
        v.liters,
        v.package_price,
        CASE
            WHEN o.supplier_price_per_liter IS NULL OR o.margin_uah_per_liter IS NULL THEN NULL
            ELSE LEAST(
                o.supplier_price_per_liter + o.margin_uah_per_liter,
                COALESCE(o.pump_price_per_liter, 1000000000) - COALESCE(o.min_discount_per_liter, 0))
        END AS rate_per_liter
    FROM fuel_offer_volumes v
    JOIN fuel_offers o ON o.id = v.offer_id
    WHERE NOT o.is_deleted
      AND NOT v.is_deleted
) AS rated
WHERE rated.rate_per_liter IS NOT NULL
  AND abs(rated.package_price - rated.rate_per_liter * rated.liters) > 0.005
ORDER BY abs(rated.package_price - rated.rate_per_liter * rated.liters) DESC,
         rated.station_id, rated.fuel_type_id, rated.liters;

COMMIT;