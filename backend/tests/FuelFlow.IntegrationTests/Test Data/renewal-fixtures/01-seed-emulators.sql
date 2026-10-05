-- =============================================================================================
-- Renewal / short-term-sale emulator fixtures
--
-- Seeds one voucher per branch the two-date model can produce, plus the stock and the negative cases
-- that a test campaign would otherwise have to invent on the fly. Every row is prefixed `EMU2-` so a
-- campaign can prove what it created and a cleanup can remove exactly what it created.
--
-- Idempotent: re-running deletes the previous EMU2 set (and only it) before inserting. Safe to run
-- against prod — it touches no non-EMU2 voucher.
--
-- Usage:
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 < 01-seed-emulators.sql
--
-- Run 00-audit-expired.sql first and read its output: this script assumes the expired backlog is
-- already cleared, so the stock it seeds is what a campaign will actually find.
--
-- Notes that cost real debugging time, kept here so the next person does not repeat it:
--
--   * `fuel_vouchers.status` is a varchar column, not the C# enum's ordinal. Values are 'Available',
--     'Assigned', 'Used', 'Blocked', 'Expired'.
--   * `fuel_types.id` is a uuid in this deployment, not a natural code like 'okko-95'.
--   * CHECK ck_voucher_held_has_order: a voucher in 'Assigned', 'Used' or 'Blocked' MUST have an
--     order_id. A customer-held voucher therefore cannot be inserted without an order behind it, which
--     is why this script seeds an order per source.
--   * A literal '%' inside a RAISE EXCEPTION message is a format placeholder and fails the statement
--     with "too few parameters specified for RAISE".
-- =============================================================================================

\set ON_ERROR_STOP on

BEGIN;

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

-- Do NOT null out order_id here. CHECK ck_voucher_held_has_order is evaluated per row on UPDATE, so
-- clearing the order on an Assigned voucher trips it immediately — and the row is about to be deleted
-- anyway. Delete the vouchers first, then the orders that were behind them.
DELETE FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%';

DELETE FROM order_line_items
WHERE order_id IN (SELECT id FROM orders WHERE idempotency_key LIKE 'emu2-%');

DELETE FROM orders WHERE idempotency_key LIKE 'emu2-%';

DELETE FROM fuel_packages WHERE id LIKE 'emu2-pkg-%';

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
    RAISE EXCEPTION 'No QA user whose phone ends in 0203 - create the test account first';
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

-- The fuel every existing voucher in this deployment uses: 'ДП ЄВРО' on station okko.
CREATE TEMP TABLE _fuel AS
SELECT id, name, station_id FROM fuel_types
WHERE id = 'fa63b7ab-2fb2-4cdc-bc71-ee1aff9a3785';

DO $$
BEGIN
  IF (SELECT count(*) FROM _fuel) = 0 THEN
    RAISE EXCEPTION 'the fixture fuel type is missing in this deployment - update the fixture ids';
  END IF;

  -- The under-cost refusal is only meaningful while the fuel is not opted in to selling under cost.
  IF EXISTS (SELECT 1 FROM fuel_types
             WHERE id = 'fa63b7ab-2fb2-4cdc-bc71-ee1aff9a3785' AND allow_below_cost) THEN
    RAISE EXCEPTION 'the fixture fuel has allow_below_cost set; the under-cost refusal cannot be tested on it';
  END IF;
END $$;

-- ---------------------------------------------------------------------------------------------
-- 2. The orders behind the customer's vouchers
--
-- CHECK ck_voucher_held_has_order demands an order_id on any Assigned/Used/Blocked voucher, and
-- renewal only accepts those statuses — so a held fixture cannot exist without a purchase behind it.
-- One Fulfilled order per source, each with the line item that produced it, so the wallet and the
-- order history look like a real purchase rather than an orphan voucher.
-- ---------------------------------------------------------------------------------------------

CREATE TEMP TABLE _src (
    voucher_number text PRIMARY KEY,
    order_id uuid NOT NULL,
    price numeric NOT NULL
);

INSERT INTO _src (voucher_number, order_id, price)
SELECT v.voucher_number, gen_random_uuid(), 999.00
FROM (VALUES
    ('EMU2-SRC-EXT-1M'),
    ('EMU2-SRC-EXT-EXACT'),
    ('EMU2-SRC-CEIL-NOROOM'),
    ('EMU2-SRC-LAPSED-CUST'),
    ('EMU2-SRC-DEAD-BOTH'),
    ('EMU2-SRC-OUTSIDE-WINDOW'),
    ('EMU2-SRC-BLOCKED'),
    ('EMU2-SRC-USED'),
    ('EMU2-SRC-COMPANY-REPLACE')
) AS v(voucher_number);

INSERT INTO orders
    (id, user_id, price, status, monobank_invoice_id, monobank_status, idempotency_key,
     created_at_utc, fulfilled_at_utc, updated_at_utc, is_deleted, kind)
SELECT s.order_id,
       q.id,
       s.price,
       'Fulfilled',
       'EMU2-INV-' || s.order_id,
       'Success',
       'emu2-' || s.voucher_number,
       now(), now(), now(),
       false,
       'Purchase'
FROM _src s
CROSS JOIN _qa q;

INSERT INTO order_line_items
    (id, order_id, provider, fuel_type_id, liters, quantity, unit_price, line_total)
SELECT gen_random_uuid(), s.order_id, f.station_id, f.id, 10, 1, 99.90, 999.00
FROM _src s
CROSS JOIN _fuel f;

-- ---------------------------------------------------------------------------------------------
-- 3. Sources: what a customer holds, and therefore what they can renew
--
-- Each row is one branch of `TryResolveBranch` plus the cases that must never renew at all.
-- `provider_expiration_date` is the ceiling; `customer_expiration_date` is what the wallet shows.
-- ---------------------------------------------------------------------------------------------

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10,
       (CURRENT_DATE + 100), (CURRENT_DATE + 10),
       'EMU2-SRC-EXT-1M', 'EMU2-QR-SRC-EXT-1M', 'Assigned',
       q.id, NULL, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s WHERE s.voucher_number = 'EMU2-SRC-EXT-1M';

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10,
       (CURRENT_DATE + 10 + 30), (CURRENT_DATE + 10),
       'EMU2-SRC-EXT-EXACT', 'EMU2-QR-SRC-EXT-EXACT', 'Assigned',
       q.id, NULL, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s WHERE s.voucher_number = 'EMU2-SRC-EXT-EXACT';

-- Customer term has caught up with the supplier's real term: no room to grow at all. Checkout sells a
-- replacement here, and fulfilment used to try to extend, hit the ceiling and stall the paid order.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10,
       (CURRENT_DATE + 13), (CURRENT_DATE + 10),
       'EMU2-SRC-CEIL-NOROOM', 'EMU2-QR-SRC-CEIL-NOROOM', 'Assigned',
       q.id, NULL, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s WHERE s.voucher_number = 'EMU2-SRC-CEIL-NOROOM';

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10,
       (CURRENT_DATE + 90), (CURRENT_DATE - 3),
       'EMU2-SRC-LAPSED-CUST', 'EMU2-QR-SRC-LAPSED-CUST', 'Assigned',
       q.id, NULL, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s WHERE s.voucher_number = 'EMU2-SRC-LAPSED-CUST';

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10,
       (CURRENT_DATE - 1), (CURRENT_DATE - 30),
       'EMU2-SRC-DEAD-BOTH', 'EMU2-QR-SRC-DEAD-BOTH', 'Assigned',
       q.id, NULL, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s WHERE s.voucher_number = 'EMU2-SRC-DEAD-BOTH';

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10,
       (CURRENT_DATE + 200), (CURRENT_DATE + 60),
       'EMU2-SRC-OUTSIDE-WINDOW', 'EMU2-QR-SRC-OUTSIDE-WINDOW', 'Assigned',
       q.id, NULL, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s WHERE s.voucher_number = 'EMU2-SRC-OUTSIDE-WINDOW';

-- Statuses that must be refused regardless of dates. Same dates as the extendable one, so the only
-- thing that can reject them is the status guard.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10, (CURRENT_DATE + 100), (CURRENT_DATE + 10),
       'EMU2-SRC-BLOCKED', 'EMU2-QR-SRC-BLOCKED', 'Blocked', q.id, NULL, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s WHERE s.voucher_number = 'EMU2-SRC-BLOCKED';

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10, (CURRENT_DATE + 100), (CURRENT_DATE + 10),
       'EMU2-SRC-USED', 'EMU2-QR-SRC-USED', 'Used', q.id, NULL, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s WHERE s.voucher_number = 'EMU2-SRC-USED';

-- Company-owned: a replacement must inherit the entity, not silently become personal.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, order_id,
     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10, (CURRENT_DATE + 90), (CURRENT_DATE - 2),
       'EMU2-SRC-COMPANY-REPLACE', 'EMU2-QR-SRC-COMPANY-REPLACE', 'Assigned',
       q.id, c.id, s.order_id, now(), now()
FROM _qa q, _fuel f, _src s, _qa_company c WHERE s.voucher_number = 'EMU2-SRC-COMPANY-REPLACE';

-- ---------------------------------------------------------------------------------------------
-- 4. Stock: what a replacement can be served from
--
-- The selector requires provider + fuel + litres to match exactly and the paper term to reach
-- `today + chosen term`. So the interesting stock is the stock that must NOT match: another
-- nominal, another fuel, another provider, and one whose paper is too short to serve a long tier.
-- Stock carries no order_id — it has never been bought by a customer.
-- ---------------------------------------------------------------------------------------------

INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10, CURRENT_DATE + 180, CURRENT_DATE + 180,
       'EMU2-STK-LONG-6M',  'EMU2-QR-STK-LONG-6M',  'Available', NULL::uuid, NULL::uuid, now(), now()
FROM _fuel f
UNION ALL SELECT gen_random_uuid(), 'OKKO', f.id, 10, CURRENT_DATE + 90, CURRENT_DATE + 90,
       'EMU2-STK-MID-3M',   'EMU2-QR-STK-MID-3M',   'Available', NULL::uuid, NULL::uuid, now(), now() FROM _fuel f
UNION ALL SELECT gen_random_uuid(), 'OKKO', f.id, 10, CURRENT_DATE + 30, CURRENT_DATE + 30,
       'EMU2-STK-EXACT-1M', 'EMU2-QR-STK-EXACT-1M', 'Available', NULL::uuid, NULL::uuid, now(), now() FROM _fuel f
UNION ALL SELECT gen_random_uuid(), 'OKKO', f.id, 10, CURRENT_DATE + 20, CURRENT_DATE + 20,
       'EMU2-STK-SHORT-20D', 'EMU2-QR-STK-SHORT-20D', 'Available', NULL::uuid, NULL::uuid, now(), now() FROM _fuel f
UNION ALL SELECT gen_random_uuid(), 'OKKO', f.id, 10, CURRENT_DATE + 5, CURRENT_DATE + 5,
       'EMU2-STK-STALE-5D',  'EMU2-QR-STK-STALE-5D',  'Available', NULL::uuid, NULL::uuid, now(), now() FROM _fuel f
UNION ALL SELECT gen_random_uuid(), 'OKKO', f.id, 20, CURRENT_DATE + 180, CURRENT_DATE + 180,
       'EMU2-STK-OTHER-20L', 'EMU2-QR-STK-OTHER-20L', 'Available', NULL::uuid, NULL::uuid, now(), now() FROM _fuel f
UNION ALL SELECT gen_random_uuid(), 'UPG', f2.id, 10, CURRENT_DATE + 180, CURRENT_DATE + 180,
       'EMU2-STK-OTHER-PROV', 'EMU2-QR-STK-OTHER-PROV', 'Available', NULL::uuid, NULL::uuid, now(), now()
       FROM fuel_types f2 WHERE f2.id = 'b7936d8d-48f3-4c35-8916-e12ab74205b8'
UNION ALL SELECT gen_random_uuid(), 'OKKO', f3.id, 10, CURRENT_DATE + 180, CURRENT_DATE + 180,
       'EMU2-STK-OTHER-FUEL', 'EMU2-QR-STK-OTHER-FUEL', 'Available', NULL::uuid, NULL::uuid, now(), now()
       FROM fuel_types f3 WHERE f3.id = '24dde716-e638-4f64-96d0-f43fb083c5f7';

-- Three identical long-dated rows for the batch scenario: every replace line needs a DISTINCT
-- voucher, so one row cannot serve a three-line order.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10, CURRENT_DATE + 180, CURRENT_DATE + 180,
       'EMU2-STK-BATCH-' || s, 'EMU2-QR-STK-BATCH-' || s, 'Available', NULL::uuid, NULL::uuid, now(), now()
FROM _fuel f, generate_series(1, 3) s;

-- Company stock: a company-owned replacement candidate, to prove the entity can be inherited from
-- either the source voucher or the stock.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, status, assigned_to_user_id, legal_entity_id, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'OKKO', f.id, 10, CURRENT_DATE + 180, CURRENT_DATE + 180,
       'EMU2-STK-COMPANY', 'EMU2-QR-STK-COMPANY', 'Available', NULL, c.id, now(), now()
FROM _fuel f, _qa_company c;

-- ---------------------------------------------------------------------------------------------
-- 5. Purchase-side catalog
--
-- Cloned from the fixture fuel so the station and fuel actually exist in this deployment, then
-- pinned to a known cost and margin: 40 ₴/L supplier cost, 20 ₴/L margin, 60 ₴/L shelf. That is what
-- makes the below-cost probes meaningful — a 20 ₴/L term discount lands exactly on cost (allowed,
-- zero margin) and 25 ₴/L goes under it (must be refused).
-- ---------------------------------------------------------------------------------------------

INSERT INTO fuel_packages
    (id, station_id, fuel_type_id, fuel_name, liters, price, original_price,
     supplier_price_per_liter, margin_uah_per_liter, final_price_per_liter, pump_price_per_liter,
     min_discount_per_liter, created_at_utc, updated_at_utc)
SELECT 'emu2-pkg-' || substr(md5(f.id), 1, 8),
       f.station_id, f.id, f.name, 10,
       600, 600,
       40, 20, 60, 60,
       0, now(), now()
FROM _fuel f;

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
FROM _fuel f;

-- ---------------------------------------------------------------------------------------------
-- 6. The purchase ladder is deliberately left alone
--
-- T0 runs with the ladder unset and must show no picker at all. Do not pre-enable it here: the point
-- of T0 is to prove the fail-safe state on a database that has never had the feature switched on.
-- Set it in admin → Settings → "Short-term fuel selling" when the campaign reaches T1.
-- ---------------------------------------------------------------------------------------------

COMMIT;

-- ---------------------------------------------------------------------------------------------
-- 7. What got created
-- ---------------------------------------------------------------------------------------------

\echo ''
\echo '=== sources (customer-owned) ==='
SELECT v.voucher_number, v.status, v.liters,
       v.customer_expiration_date AS customer_term,
       v.provider_expiration_date AS provider_term,
       (v.provider_expiration_date - v.customer_expiration_date) AS room,
       v.legal_entity_id IS NOT NULL AS in_company
FROM fuel_vouchers v
WHERE v.voucher_number LIKE 'EMU2-SRC-%'
ORDER BY v.voucher_number;

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
\echo '=== orders behind the held vouchers ==='
SELECT count(*) AS orders, count(*) FILTER (WHERE status = 'Fulfilled') AS fulfilled
FROM orders WHERE idempotency_key LIKE 'emu2-%';