-- =============================================================================================
-- Renewal / short-term-sale emulator fixtures
--
-- Seeds one voucher per branch the two-date model can produce, plus the stock and the negative cases
-- that a test campaign would otherwise have to invent on the fly. Every row is prefixed `EMU2-` so a
-- campaign can prove what it created and a cleanup can remove exactly what it created.
--
-- Idempotent: re-running deletes the previous EMU2 set (and only it) before inserting. Safe to run
-- against prod — it touches no non-EMU2 voucher and creates no order.
--
-- Usage:
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 < 01-seed-emulators.sql
--
-- Run 00-audit-expired.sql first and read its output: this script assumes the expired backlog is
-- already cleared, so the stock it seeds is what a campaign will actually find.
-- =============================================================================================

\set ON_ERROR_STOP on

BEGIN;

-- Bump the campaign marker so a later run supersedes an earlier one.
CREATE TEMP TABLE _emu_marker AS
SELECT 'EMU2-' || to_char(now(), 'YYYYMMDD') || '-' || substr(md5(random()::text), 1, 6) AS tag;

-- ---------------------------------------------------------------------------------------------
-- 0. Clear the previous EMU2 set
--
-- Only rows whose voucher_number carries the prefix, and only once every reference to them is gone.
-- The order matters: fulfillments and renewal items point at vouchers with ON DELETE RESTRICT, so
-- they have to be unhooked before the voucher can be dropped.
-- ---------------------------------------------------------------------------------------------

DELETE FROM fulfillments
WHERE voucher_id IN (SELECT id FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%');

DELETE FROM voucher_renewal_items
WHERE source_voucher_id IN (SELECT id FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%')
   OR fulfilled_voucher_id IN (SELECT id FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%');

UPDATE voucher_exchanges
SET new_voucher_id = NULL
WHERE new_voucher_id IN (SELECT id FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%');

DELETE FROM voucher_exchanges
WHERE old_voucher_id IN (SELECT id FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%');

DELETE FROM operator_voucher_renewals
WHERE voucher_id IN (SELECT id FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%');

-- The order row is kept if one exists; only the stale pointer is cleared, so an order created by a
-- previous campaign against an EMU2 voucher cannot block cleanup but also cannot dangle.
UPDATE fuel_vouchers SET order_id = NULL
WHERE voucher_number LIKE 'EMU2-%' AND order_id IS NOT NULL;

DELETE FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%';

-- ---------------------------------------------------------------------------------------------
-- 1. The customer under test
--
-- Resolved, never hardcoded: the phone lives in the test-plan doc and will not stay the same.
-- ---------------------------------------------------------------------------------------------

CREATE TEMP TABLE _qa AS
SELECT id, phone_number FROM users WHERE phone_number LIKE '%0203';

DO $$
BEGIN
  IF (SELECT count(*) FROM _qa) = 0 THEN
    RAISE EXCEPTION 'No QA user matching %0203 — create the test account first';
  END IF;
END $$;

-- The company whose stock must stay company stock after a replacement (scenario M7).
CREATE TEMP TABLE _qa_company AS
SELECT le.id
FROM legal_entities le
WHERE EXISTS (
    SELECT 1 FROM company_members m
    WHERE m.legal_entity_id = le.id
      AND m.worker_user_id = (SELECT id FROM _qa)
)
LIMIT 1;

-- ---------------------------------------------------------------------------------------------
-- 2. Sources: what a customer holds, and therefore what they can renew
--
-- Each row is one branch of `TryResolveBranch` plus the cases that must never renew at all.
-- `provider_expiration_date` is the ceiling; `customer_expiration_date` is what the wallet shows.
-- ---------------------------------------------------------------------------------------------

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(), 'OKKO', 'okko-95', 10,
    (CURRENT_DATE + 100), (CURRENT_DATE + 10),
    'EMU2-SRC-EXT-1M', 'EMU2-QR-SRC-EXT-1M', 4,
    q.id, NULL, now(), now()
FROM _qa q;

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(), 'OKKO', 'okko-95', 10,
    (CURRENT_DATE + 10 + 30), (CURRENT_DATE + 10),
    'EMU2-SRC-EXT-EXACT', 'EMU2-QR-SRC-EXT-EXACT', 4,
    q.id, NULL, now(), now()
FROM _qa q;

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(), 'OKKO', 'okko-95', 10,
    (CURRENT_DATE + 13), (CURRENT_DATE + 10),
    'EMU2-SRC-CEIL-NOROOM', 'EMU2-QR-SRC-CEIL-NOROOM', 4,
    q.id, NULL, now(), now()
FROM _qa q;

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(), 'OKKO', 'okko-95', 10,
    (CURRENT_DATE + 90), (CURRENT_DATE - 3),
    'EMU2-SRC-LAPSED-CUST', 'EMU2-QR-SRC-LAPSED-CUST', 4,
    q.id, NULL, now(), now()
FROM _qa q;

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(), 'OKKO', 'okko-95', 10,
    (CURRENT_DATE - 1), (CURRENT_DATE - 30),
    'EMU2-SRC-DEAD-BOTH', 'EMU2-QR-SRC-DEAD-BOTH', 4,
    q.id, NULL, now(), now()
FROM _qa q;

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(), 'OKKO', 'okko-95', 10,
    (CURRENT_DATE + 200), (CURRENT_DATE + 60),
    'EMU2-SRC-OUTSIDE-WINDOW', 'EMU2-QR-SRC-OUTSIDE-WINDOW', 4,
    q.id, NULL, now(), now()
FROM _qa q;

-- Statuses that must be refused regardless of dates. Same dates as the extendable one, so the only
-- thing that can reject them is the status guard.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', 'okko-95', 10, (CURRENT_DATE + 100), (CURRENT_DATE + 10),
       'EMU2-SRC-BLOCKED', 'EMU2-QR-SRC-BLOCKED', 7, q.id, NULL, now(), now()
FROM _qa q;

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', 'okko-95', 10, (CURRENT_DATE + 100), (CURRENT_DATE + 10),
       'EMU2-SRC-USED', 'EMU2-QR-SRC-USED', 5, q.id, NULL, now(), now()
FROM _qa q;

-- Company-owned: a replacement must inherit the entity, not silently become personal.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', 'okko-95', 10, (CURRENT_DATE + 90), (CURRENT_DATE - 2),
       'EMU2-SRC-COMPANY-REPLACE', 'EMU2-QR-SRC-COMPANY-REPLACE', 4, q.id, c.id, now(), now()
FROM _qa q, _qa_company c;

-- ---------------------------------------------------------------------------------------------
-- 3. Stock: what a replacement can be served from
--
-- The selector requires provider + fuel + litres to match exactly and the paper term to reach
-- `today + chosen term`. So the interesting stock is the stock that must NOT match: another
-- nominal, another fuel, another provider, and one whose paper is too short to serve a long tier.
-- ---------------------------------------------------------------------------------------------

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
VALUES
  (gen_random_uuid(), 'OKKO', 'okko-95', 10, CURRENT_DATE + 180, CURRENT_DATE + 180, 'EMU2-STK-LONG-6M',  'EMU2-QR-STK-LONG-6M',  3, NULL, NULL, now(), now()),
  (gen_random_uuid(), 'OKKO', 'okko-95', 10, CURRENT_DATE + 90,  CURRENT_DATE + 90,  'EMU2-STK-MID-3M',   'EMU2-QR-STK-MID-3M',   3, NULL, NULL, now(), now()),
  (gen_random_uuid(), 'OKKO', 'okko-95', 10, CURRENT_DATE + 30,  CURRENT_DATE + 30,  'EMU2-STK-EXACT-1M', 'EMU2-QR-STK-EXACT-1M', 3, NULL, NULL, now(), now()),
  (gen_random_uuid(), 'OKKO', 'okko-95', 10, CURRENT_DATE + 20,  CURRENT_DATE + 20,  'EMU2-STK-SHORT-20D', 'EMU2-QR-STK-SHORT-20D', 3, NULL, NULL, now(), now()),
  (gen_random_uuid(), 'OKKO', 'okko-95', 10, CURRENT_DATE + 5,   CURRENT_DATE + 5,   'EMU2-STK-STALE-5D',  'EMU2-QR-STK-STALE-5D',  3, NULL, NULL, now(), now()),
  (gen_random_uuid(), 'OKKO', 'okko-95', 20, CURRENT_DATE + 180, CURRENT_DATE + 180, 'EMU2-STK-OTHER-20L', 'EMU2-QR-STK-OTHER-20L', 3, NULL, NULL, now(), now()),
  (gen_random_uuid(), 'SHELL','shell-95', 10, CURRENT_DATE + 180, CURRENT_DATE + 180, 'EMU2-STK-OTHER-PROV','EMU2-QR-STK-OTHER-PROV',3, NULL, NULL, now(), now()),
  (gen_random_uuid(), 'OKKO', 'okko-diesel', 10, CURRENT_DATE + 180, CURRENT_DATE + 180, 'EMU2-STK-OTHER-FUEL','EMU2-QR-STK-OTHER-FUEL',3, NULL, NULL, now(), now());

-- Three identical long-dated rows for the batch scenario: every replace line needs a DISTINCT
-- voucher, so one row cannot serve a three-line order.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', 'okko-95', 10, CURRENT_DATE + 180, CURRENT_DATE + 180,
       'EMU2-STK-BATCH-' || s, 'EMU2-QR-STK-BATCH-' || s, 3, NULL, NULL, now(), now()
FROM generate_series(1, 3) s;

-- Company stock: a company-owned replacement candidate, to prove the entity can be inherited from
-- either the source voucher or the stock.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', 'okko-95', 10, CURRENT_DATE + 180, CURRENT_DATE + 180,
       'EMU2-STK-COMPANY', 'EMU2-QR-STK-COMPANY', 3, NULL, c.id, now(), now()
FROM _qa_company c;

-- ---------------------------------------------------------------------------------------------
-- 4. Purchase-side catalog
--
-- Cloned from an existing package so the station and fuel actually exist in this deployment, then
-- pinned to a known cost and margin: 40 ₴/L supplier cost, 20 ₴/L margin, 60 ₴/L shelf. That is what
-- makes the below-cost probes meaningful — a 20 ₴/L term discount lands exactly on cost (allowed,
-- zero margin) and 25 ₴/L goes under it (must be refused).
--
-- Only safe while the fuel is NOT opted in to selling under cost. If `allow_below_cost` is true the
-- guard is bypassed for this fuel by design, and the "must be refused" half of the below-cost scenario
-- cannot be tested against it. The check below stops the seed rather than seeding a fixture that would
-- quietly pass for the wrong reason.
-- ---------------------------------------------------------------------------------------------

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM fuel_types WHERE id = 'okko-95') THEN
    RAISE EXCEPTION 'fuel type okko-95 does not exist in this deployment — change the fixture ids';
  END IF;

  IF EXISTS (SELECT 1 FROM fuel_types WHERE id = 'okko-95' AND allow_below_cost) THEN
    RAISE EXCEPTION 'okko-95 has allow_below_cost = true; the under-cost refusal cannot be tested on it';
  END IF;
END $$;

INSERT INTO fuel_packages
    (id, station_id, fuel_type_id, fuel_name, liters, price, original_price,
     supplier_price_per_liter, margin_uah_per_liter, final_price_per_liter, pump_price_per_liter,
     min_discount_per_liter, created_at_utc, updated_at_utc)
SELECT 'emu2-pkg-' || substr(md5(f.id), 1, 8),
       f.station_id, f.id, f.name, 10,
       600, 600,
       40, 20, 60, 60,
       0, now(), now()
FROM fuel_types f
WHERE f.id = 'okko-95'
ON CONFLICT (id) DO UPDATE
SET supplier_price_per_liter = EXCLUDED.supplier_price_per_liter,
    margin_uah_per_liter = EXCLUDED.margin_uah_per_liter,
    final_price_per_liter = EXCLUDED.final_price_per_liter,
    price = EXCLUDED.price,
    original_price = EXCLUDED.original_price,
    updated_at_utc = now();

-- A 20 L sibling so a cart can hold two lines of different nominals — and, with the ladder on, two
-- different terms in one order.
INSERT INTO fuel_packages
    (id, station_id, fuel_type_id, fuel_name, liters, price, original_price,
     supplier_price_per_liter, margin_uah_per_liter, final_price_per_liter, pump_price_per_liter,
     min_discount_per_liter, created_at_utc, updated_at_utc)
SELECT 'emu2-pkg-' || substr(md5(f.id), 1, 8) || '-20l',
       f.station_id, f.id, f.name, 20,
       1200, 1200,
       40, 20, 60, 60,
       0, now(), now()
FROM fuel_types f
WHERE f.id = 'okko-95'
ON CONFLICT (id) DO UPDATE
SET supplier_price_per_liter = EXCLUDED.supplier_price_per_liter,
    margin_uah_per_liter = EXCLUDED.margin_uah_per_liter,
    final_price_per_liter = EXCLUDED.final_price_per_liter,
    price = EXCLUDED.price,
    original_price = EXCLUDED.original_price,
    updated_at_utc = now();

-- ---------------------------------------------------------------------------------------------
-- 5. The purchase ladder is deliberately left alone
--
-- T0 runs with the ladder unset and must show no picker at all. Do not pre-enable it here: the point
-- of T0 is to prove the fail-safe state on a database that has never had the feature switched on.
-- Set it in admin → Settings → "Short-term fuel selling" when the campaign reaches T1.
-- ---------------------------------------------------------------------------------------------

COMMIT;

-- ---------------------------------------------------------------------------------------------
-- 6. What got created
-- ---------------------------------------------------------------------------------------------

\echo ''
\echo '=== sources (customer-owned) ==='
SELECT voucher_number, status, liters,
       customer_expiration_date AS customer_term,
       provider_expiration_date AS provider_term,
       (provider_expiration_date - customer_expiration_date) AS room,
       legal_entity_id IS NOT NULL AS in_company
FROM fuel_vouchers
WHERE voucher_number LIKE 'EMU2-SRC-%'
ORDER BY voucher_number;

\echo ''
\echo '=== stock (unowned) ==='
SELECT voucher_number, liters, provider_expiration_date AS provider_term,
       (provider_expiration_date - CURRENT_DATE) AS covers_days
FROM fuel_vouchers
WHERE voucher_number LIKE 'EMU2-STK-%'
ORDER BY voucher_number;

\echo ''
\echo '=== packages ==='
SELECT id, liters, price, supplier_price_per_liter AS cost, final_price_per_liter AS shelf
FROM fuel_packages
WHERE id LIKE 'emu2-pkg-%'
ORDER BY id;

\echo ''
\echo '=== the fuel these packages hang off ==='
SELECT id, name, station_id, base_price, allow_below_cost
FROM fuel_types
WHERE id = 'okko-95';