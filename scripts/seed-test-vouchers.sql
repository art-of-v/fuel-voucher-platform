-- Seeds emulated test vouchers for a QA account and flags them `is_test_data`, so a QA run can never
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

-- The fuel type is the OKKO one the campaign has always used; overridable if the catalog changes.
\if :{?fuel_type_id}
\else
\let fuel_type_id 'okko-95'
\endif

-- A single purchase order holding two vouchers: one with room to extend (customer term well short of
-- the provider term) and one whose customer term has caught up with it, which is the case that must
-- take the replace branch. The third voucher is already lapsed, so it exercises replace on entry.
--
-- `current_date + 7` keeps both inside the renewal trigger window (14 days by default): a customer
-- term further out shows no renewal entry point at all, which is correct but not what a test wants.
INSERT INTO orders (id, user_id, price, status, kind, created_at_utc, updated_at_utc, fulfilled_at_utc, is_deleted)
VALUES (
    '11111111-1111-4111-8111-000000000001', :'qa_user_id', 2907.00, 'Fulfilled', 'Purchase',
    now() - interval '3 days', now(), now() - interval '3 days', false)
ON CONFLICT (id) DO NOTHING;

INSERT INTO order_line_items (id, order_id, provider, fuel_type_id, liters, quantity, unit_price, line_total, term_code)
VALUES (
    '44444444-4444-4444-8444-000000000001', '11111111-1111-4111-8111-000000000001', 'OKKO',
    :'fuel_type_id', 10.00, 3, 96.90, 2907.00, '1w')
ON CONFLICT (id) DO NOTHING;

-- Held by the QA account (is_test_data true), plus operator stock for the replace branch. The stock
-- rows are flagged too: a QA account may only draw on test fuel, and unflagged test stock would be
-- indistinguishable from the real batch.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, provider_expiration_date, customer_expiration_date,
     voucher_number, external_id, qr_payload, created_at_utc, updated_at_utc, status, order_id,
     assigned_to_user_id, legal_entity_id, worker_user_id, is_test_data, is_deleted)
VALUES
    -- room to extend: customer +7d, provider +90d
    ('22222222-2222-4222-8222-000000000001', 'OKKO', :'fuel_type_id', 10.00, current_date + 90, current_date + 7,
     'EMU-VCH-EXT', 'EMU-VCH-EXT', 'EMU-QR-EXT', now() - interval '3 days', now(), 'Assigned',
     '11111111-1111-4111-8111-000000000001', :'qa_user_id', NULL, NULL, true, false),
    -- ceiling reached while still valid: must refuse to extend and replace instead
    ('22222222-2222-4222-8222-000000000002', 'OKKO', :'fuel_type_id', 10.00, current_date + 7, current_date + 7,
     'EMU-VCH-FULL', 'EMU-VCH-FULL', 'EMU-QR-FULL', now() - interval '3 days', now(), 'Assigned',
     '11111111-1111-4111-8111-000000000001', :'qa_user_id', NULL, NULL, true, false),
    -- already lapsed: the replace branch on entry
    ('22222222-2222-4222-8222-000000000003', 'OKKO', :'fuel_type_id', 10.00, current_date + 90, current_date - 2,
     'EMU-VCH-LAPSED', 'EMU-VCH-LAPSED', 'EMU-QR-LAPSED', now() - interval '20 days', now(), 'Expired',
     '11111111-1111-4111-8111-000000000001', NULL, NULL, NULL, true, false),
    -- stock for a replacement, long enough for any tier up to 6 months
    ('33333333-3333-4333-8333-000000000001', 'OKKO', :'fuel_type_id', 10.00, current_date + 180, current_date + 180,
     'EMU-STK-A', 'EMU-STK-A', 'EMU-QR-STK-A', now(), now(), 'Available', NULL, NULL, NULL, NULL, true, false),
    ('33333333-3333-4333-8333-000000000002', 'OKKO', :'fuel_type_id', 10.00, current_date + 180, current_date + 180,
     'EMU-STK-B', 'EMU-STK-B', 'EMU-QR-STK-B', now(), now(), 'Available', NULL, NULL, NULL, NULL, true, false),
    -- short paper term: buying a longer tier must clamp to this date, never past it
    ('33333333-3333-4333-8333-000000000003', 'OKKO', :'fuel_type_id', 10.00, current_date + 20, current_date + 20,
     'EMU-STK-SHORT', 'EMU-STK-SHORT', 'EMU-QR-STK-SHORT', now(), now(), 'Available', NULL, NULL, NULL, NULL, true, false)
ON CONFLICT (id) DO NOTHING;

-- The wallet nests vouchers by reading fulfillments, not fuel_vouchers.order_id: without these rows
-- the order renders as an empty card.
INSERT INTO fulfillments (id, order_id, voucher_id, fulfilled_at_utc)
VALUES
    ('55555555-5555-4555-8555-000000000001', '11111111-1111-4111-8111-000000000001', '22222222-2222-4222-8222-000000000001', now() - interval '3 days'),
    ('55555555-5555-4555-8555-000000000002', '11111111-1111-4111-8111-000000000001', '22222222-2222-4222-8222-000000000002', now() - interval '3 days'),
    ('55555555-5555-4555-8555-000000000003', '11111111-1111-4111-8111-000000000001', '22222222-2222-4222-8222-000000000003', now() - interval '20 days')
ON CONFLICT (id) DO NOTHING;

\echo '--- seeded ---'
SELECT voucher_number, status, customer_expiration_date AS customer, provider_expiration_date AS provider, is_test_data
FROM fuel_vouchers WHERE is_test_data ORDER BY voucher_number;

SELECT 'test_vouchers_total' AS check, count(*) FROM fuel_vouchers WHERE is_test_data;
SELECT 'real_vouchers_total' AS check, count(*) FROM fuel_vouchers WHERE NOT is_test_data AND NOT is_deleted;

COMMIT;