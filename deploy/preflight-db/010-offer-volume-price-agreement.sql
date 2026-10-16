-- Data preconditions, checked BEFORE a new image is pulled.
--
-- One row per live volume whose package price disagrees with the per-litre price of the offer it
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
-- WHAT CHANGED, AND WHY THE QUESTION CHANGED. This used to count how many distinct values a
-- (station, fuel, pool) group held for each per-litre column on `fuel_packages`. That question
-- is now unaskable, and in the strongest possible sense: per-litre pricing moved onto
-- `fuel_offers`, and the unique index on (station_id, fuel_type_id, data_kind) means one group
-- is one row. The disagreement those columns could hold cannot be represented.
--
-- What CAN still disagree is the pair that now carries the price in two places. An offer names a
-- price per litre; a volume names what that nominal costs in total. Nothing ties them together -
-- nothing did before either, but before it did not matter, because the volume's own per-litre
-- column was what the storefront read. Now the offer's rate is what `ServerPricing` charges and
-- the volume's total is what the shelf shows, so a volume that disagrees with its offer advertises
-- one number and charges another. That is the drift worth blocking a deploy for, and it is money,
-- so it is reported in money: `discrepancy_uah` is what the customer would be over- or
-- under-charged, not a ratio.
--
-- OFFERS WITH NO FINAL PRICE ARE SKIPPED, not reported. `final_price_per_liter` is NULL for the
-- legacy cost-plus rows the pricing migration deliberately left alone, and for an offer with no
-- pump price there is no rate to disagree with. Reporting them would fire the gate on rows the
-- system deliberately permits, and a gate that cries wolf on the permitted state is a gate people
-- learn to click past.
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

BEGIN;
SET TRANSACTION READ ONLY;

SELECT
    o.id                                                           AS offer_id,
    o.station_id,
    o.fuel_type_id,
    o.data_kind,
    v.liters,
    o.final_price_per_liter,
    v.package_price,
    -- What the offer's rate says this nominal costs, against what the volume says it costs. The
    -- whole row exists because these two differ.
    round(o.final_price_per_liter * v.liters, 2)                   AS price_from_rate,
    round(v.package_price - o.final_price_per_liter * v.liters, 2) AS discrepancy_uah
FROM fuel_offer_volumes v
JOIN fuel_offers o ON o.id = v.offer_id
WHERE NOT o.is_deleted
  AND NOT v.is_deleted
  AND o.final_price_per_liter IS NOT NULL
  AND abs(v.package_price - o.final_price_per_liter * v.liters) > 0.005
ORDER BY abs(v.package_price - o.final_price_per_liter * v.liters) DESC,
         o.station_id, o.fuel_type_id, v.liters;

COMMIT;
