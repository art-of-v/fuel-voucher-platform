-- Data preconditions, checked BEFORE a new image is pulled.
--
-- One row per offer whose `station_id` disagrees with the station of the fuel type it sells.
-- Zero rows means every offer's station is the fuel type's own.
--
-- WHY THIS EXISTS. `fuel_offers.id` is the fuel type id plus the pool suffix (`OfferKeys`), with
-- no station in it - since `DropTheStationPrefixFromOfferKeys`, 2026-10-17, which replaced the
-- station-prefixed key that read `okko-okko-95`. The key is safe because `fuel_offers.station_id`
-- mirrors `fuel_types.station_id`: a fuel type belongs to exactly one station, so its id already
-- determines which station the offer is at.
--
-- No constraint can hold that up. `fuel_offers.station_id` is a plain text column and its
-- agreement with `fuel_types.station_id` is a fact about two tables, which is precisely the
-- class of thing a CHECK cannot see. So the premise is asked here instead, where a row is a
-- sentence a human can act on rather than an exception at boot.
--
-- IF ONE APPEARS, the offer id is ambiguous: the same fuel type would be sold by two networks,
-- and the offer's key cannot separate them. Either the offer's station is wrong, or the schema
-- has to go back to spelling the station out in the key. The first is a data fix; the second is
-- a migration - so read which it is before touching the row.
--
-- ARCHIVED ROWS ARE INCLUDED, unlike the rest of this directory: an archived offer keeps its key
-- for ever, so an archived disagreement is still a disagreement about which key is correct. It
-- cannot publish a price, so it must not block a deploy either - which is why this is a
-- question, not a gate.

BEGIN;
SET TRANSACTION READ ONLY;

SELECT
    o.id            AS offer_id,
    o.station_id    AS offer_station,
    f.station_id    AS fuel_type_station,
    o.fuel_type_id,
    o.data_kind,
    o.is_deleted
FROM fuel_offers o
JOIN fuel_types f ON f.id = o.fuel_type_id
WHERE o.station_id <> f.station_id
ORDER BY o.station_id, o.fuel_type_id, o.id;

COMMIT;