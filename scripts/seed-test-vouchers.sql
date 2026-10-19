-- Seeds emulated test vouchers for a QA account and gives them `data_kind = 'qa'`, so a QA run can never
-- touch real fuel and real fuel can never reach a QA tester.
--
-- The flag is what makes this safe to leave in a database that also holds real stock: the admin list
-- can filter on it, and the claim guard (a follow-up) refuses to hand an emulated voucher to a
-- paying customer. Nothing here is ever set on imported stock.
--
-- Usage:
--   docker exec -i <pg-container> psql -U <user> -d <db> \
--     -v qa_user_id=<uuid-of-the-qa-account> \
--     -f - < scripts/seed-test-vouchers.sql
--
-- `qa_user_id` is required and deliberately not defaulted: seeding against the wrong account is
-- exactly the mistake this script exists to prevent. Find it with:
--   SELECT id, phone_number FROM users WHERE is_qa_account AND NOT is_deleted;

\set ON_ERROR_STOP on
\if :{?qa_user_id}
\else
\echo 'qa_user_id is required (see the header of this file)'
\quit
\endif

BEGIN;

-- The fuel. **Resolved at run time, and required rather than guessed.**
--
-- It used to be a hardcoded id, and every value it could hold was wrong somewhere: the historical
-- staging default 'okko-95' violates the foreign key on production, and the production UUID that
-- fixed that (#943) is one environment's catalogue id - the same hazard wearing a different value. A
-- catalog id is a magic string nothing checks, and this file is the proof: it broke on staging, then
-- on production, and each fix moved the breakage rather than removing it. Planning #140, item 3.
--
-- Pass either an id, or a name to look up:
--   -v fuel_type_id=f95e054e-f254-4045-93b2-54ae207218a0
--   -v fuel_name='А95 ЄВРО'   -v fuel_brand=okko     (brand defaults to okko)
--
-- `fuel_name` is **required**, not defaulted, and that is the honest state of the catalogue today: the
-- same fuel is 'okko-95' / "A-95" in a freshly migrated database and 'f95e054e-…' / "А95 ЄВРО" in
-- production, so a default name here would be another environment's magic string. Zero matches or
-- more than one is an error that prints the candidates — a seed that quietly picked one of two A-95s
-- is how a QA campaign prices against the wrong fuel. Filling `fuel_types.brand_fuel_code` for the
-- is how a QA campaign prices against the wrong fuel. Filling `fuel_types.brand_fuel_code` for the
-- seeded fuels (planning #349, step 6) is what makes a default possible again.
--
-- `\set`, not `\let`: \let only exists from psql 15, and the deployed Postgres container rejects it
-- outright with `invalid command \let`, taking the whole seed down with it.
\if :{?fuel_type_id}
\else
  \if :{?fuel_name}
  \else
    \echo 'Pass -v fuel_name=<the fuel at this brand> or -v fuel_type_id=<id>. Neither id nor name is'
    \echo 'the same in every environment, so this file refuses to guess one - see the header.'
    \quit
  \endif
  \if :{?fuel_brand}
  \else
    \set fuel_brand 'okko'
  \endif

  -- One query, three answers, because psql's `\if` takes a BOOLEAN and not an arithmetic comparison
  -- ("\if :count = 0" is a parse error, and an unset variable is false, so a broken guard reads as a
  -- passing one). The booleans are computed here, where SQL can compare, and `\gset` assigns them.
  --
  -- The brand is matched through `brands.slug`, because `fuel_types.station_id` is gone: the brand
  -- became a row of its own with a uuid id and the slug ('okko') kept the text the network was
  -- known by. The value this script is given has not changed - only the way it is looked up has.
  SELECT
    count(*) <> 1                       AS fuel_not_resolved,
    count(*) > 1                        AS fuel_ambiguous,
    coalesce(min(f.id), '')             AS fuel_type_id
  FROM fuel_types f
  JOIN brands b ON b.id = f.brand_id
  WHERE lower(b.slug) = lower(:'fuel_brand') AND NOT f.is_deleted
    AND lower(f.name) = lower(:'fuel_name')
  \gset

  \if :fuel_not_resolved
    \echo 'No live fuel type matches that brand and name. Candidates at this brand:'
    SELECT f.id, f.name FROM fuel_types f
      JOIN brands b ON b.id = f.brand_id
      WHERE lower(b.slug) = lower(:'fuel_brand') AND NOT f.is_deleted ORDER BY f.name;
    \quit
  \endif

  \if :fuel_ambiguous
    \echo 'More than one live fuel type matches that brand and name - refusing to pick:'
    SELECT f.id, f.name FROM fuel_types f
      JOIN brands b ON b.id = f.brand_id
      WHERE lower(b.slug) = lower(:'fuel_brand') AND NOT f.is_deleted AND lower(f.name) = lower(:'fuel_name');
    \quit
  \endif
\endif

-- **The order line's quantity must equal the number of fulfillments below.**
-- `BackgroundJobs/FulfillmentService.cs` reconciles fulfillments against line items every cycle and
-- releases any voucher it counts as excess back to `Available` — a seed with quantity 1 and four
-- fulfillments loses three vouchers in about nine seconds, which reads as silent data loss and is not.

-- A single purchase order holding two vouchers: one with room to extend (customer term well short of
-- the brand term) and one whose customer term has caught up with it, which is the case that must
-- take the replace branch. The third voucher is already lapsed, so it exercises replace on entry.
--
-- `current_date + 7` keeps both inside the renewal trigger window (14 days by default): a customer
-- term further out shows no renewal entry point at all, which is correct but not what a test wants.
INSERT INTO orders (id, user_id, price, status, kind, created_at_utc, updated_at_utc, fulfilled_at_utc, is_deleted)
VALUES (
    '11111111-1111-4111-8111-000000000001', :'qa_user_id', 2907.00, 'Fulfilled', 'Purchase',
    now() - interval '3 days', now(), now() - interval '3 days', false)
ON CONFLICT (id) DO NOTHING;

INSERT INTO order_line_items (id, order_id, brand, fuel_type_id, liters, quantity, unit_price, line_total, term_code)
VALUES (
    '44444444-4444-4444-8444-000000000001', '11111111-1111-4111-8111-000000000001', 'OKKO',
    :'fuel_type_id', 10.00, 3, 96.90, 2907.00, '1w')
ON CONFLICT (id) DO NOTHING;

-- Held by the QA account (data_kind qa), plus operator stock for the replace branch. The stock
-- rows are flagged too: a QA account may only draw on test fuel, and unflagged test stock would be
-- indistinguishable from the real batch.
--
-- `qr_payload_sha256` is written because the column is NOT NULL, and the payload it hashes is the
-- emulated string beside it — the same expression `DataKindBackfillTests` uses. Leaving it out was
-- one of three ways this file could not run at all: a script nobody executes is a script nobody
-- notices is broken, and #200 closed on the two defects that were found by hand while two more sat
-- behind them.
INSERT INTO fuel_vouchers
    (id, brand, fuel_type_id, liters, supplier_expiration_date, customer_expiration_date,
     voucher_number, qr_payload, qr_payload_sha256, created_at_utc, updated_at_utc, status, order_id,
     assigned_to_user_id, legal_entity_id, worker_user_id, data_kind, is_deleted)
VALUES
    -- room to extend: customer +7d, brand +90d
    ('22222222-2222-4222-8222-000000000001', 'OKKO', :'fuel_type_id', 10.00, current_date + 90, current_date + 7,
     'EMU-VCH-EXT', 'EMU-QR-EXT', sha256(convert_to('EMU-QR-EXT', 'UTF8')), now() - interval '3 days', now(), 'Assigned',
     '11111111-1111-4111-8111-000000000001', :'qa_user_id', NULL, NULL, 'qa', false),
    -- ceiling reached while still valid: must refuse to extend and replace instead
    ('22222222-2222-4222-8222-000000000002', 'OKKO', :'fuel_type_id', 10.00, current_date + 7, current_date + 7,
     'EMU-VCH-FULL', 'EMU-QR-FULL', sha256(convert_to('EMU-QR-FULL', 'UTF8')), now() - interval '3 days', now(), 'Assigned',
     '11111111-1111-4111-8111-000000000001', :'qa_user_id', NULL, NULL, 'qa', false),
    -- already lapsed: the replace branch on entry
    ('22222222-2222-4222-8222-000000000003', 'OKKO', :'fuel_type_id', 10.00, current_date + 90, current_date - 2,
     'EMU-VCH-LAPSED', 'EMU-QR-LAPSED', sha256(convert_to('EMU-QR-LAPSED', 'UTF8')), now() - interval '20 days', now(), 'Expired',
     '11111111-1111-4111-8111-000000000001', NULL, NULL, NULL, 'qa', false),
    -- stock for a replacement, long enough for any tier up to 6 months
    ('33333333-3333-4333-8333-000000000001', 'OKKO', :'fuel_type_id', 10.00, current_date + 180, current_date + 180,
     'EMU-STK-A', 'EMU-QR-STK-A', sha256(convert_to('EMU-QR-STK-A', 'UTF8')), now(), now(), 'Available', NULL, NULL, NULL, NULL, 'qa', false),
    ('33333333-3333-4333-8333-000000000002', 'OKKO', :'fuel_type_id', 10.00, current_date + 180, current_date + 180,
     'EMU-STK-B', 'EMU-QR-STK-B', sha256(convert_to('EMU-QR-STK-B', 'UTF8')), now(), now(), 'Available', NULL, NULL, NULL, NULL, 'qa', false),
    -- short paper term: buying a longer tier must clamp to this date, never past it
    -- ('qa', not `true`: `data_kind` is the text column the boolean `is_test_data` became, and this
    -- row still carried the old boolean — which Postgres coerces to 'true' and
    -- ck_fuel_vouchers_data_kind then refuses. The third way this file could not run.)
    ('33333333-3333-4333-8333-000000000003', 'OKKO', :'fuel_type_id', 10.00, current_date + 20, current_date + 20,
     'EMU-STK-SHORT', 'EMU-QR-STK-SHORT', sha256(convert_to('EMU-QR-STK-SHORT', 'UTF8')), now(), now(), 'Available', NULL, NULL, NULL, NULL, 'qa', false)
ON CONFLICT (id) DO NOTHING;

-- The wallet nests vouchers by reading fulfillments, not fuel_vouchers.order_id: without these rows
-- the order renders as an empty card.
--
-- `id` is deliberately absent: it is an integer identity, so the historical UUIDs here were rejected
-- outright with `invalid input syntax for type integer`. One fulfillment per voucher above, which is
-- what the order line's quantity 3 declares.
INSERT INTO fulfillments (order_id, voucher_id, fulfilled_at_utc)
VALUES
    ('11111111-1111-4111-8111-000000000001', '22222222-2222-4222-8222-000000000001', now() - interval '3 days'),
    ('11111111-1111-4111-8111-000000000001', '22222222-2222-4222-8222-000000000002', now() - interval '3 days'),
    ('11111111-1111-4111-8111-000000000001', '22222222-2222-4222-8222-000000000003', now() - interval '20 days')
ON CONFLICT DO NOTHING;

\echo '--- seeded ---'
SELECT voucher_number, status, customer_expiration_date AS customer, supplier_expiration_date AS brand, data_kind
FROM fuel_vouchers WHERE data_kind = 'qa' ORDER BY voucher_number;

SELECT 'test_vouchers_total' AS check, count(*) FROM fuel_vouchers WHERE data_kind = 'qa';
SELECT 'real_vouchers_total' AS check, count(*) FROM fuel_vouchers WHERE data_kind = 'real' AND NOT is_deleted;

COMMIT;