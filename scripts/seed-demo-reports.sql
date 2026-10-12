-- Seeds one full month of payment history so the reports can be looked at rather than guessed at.
--
-- WHY THIS EXISTS
--   Every report in the admin is empty on a database that has never taken money, and an empty
--   report cannot be judged: a blank reconciliation panel and a broken one look the same. This
--   seeds the PREVIOUS calendar month - the period the statement act opens on by default - with
--   one of everything the reports distinguish between, including the cases that are supposed to
--   look wrong:
--
--     * payments that agree with the acquirer             -> "Matched"
--     * ours 979.00 against the acquirer's 978.57         -> "Amount mismatch"
--     * acquirer money matching nothing of ours           -> "Unmatched"
--     * a refund the acquirer reversed                    -> "Refund match"
--     * an order we believe was paid and the acquirer
--       never mentioned                                  -> "never mentioned"
--     * an unpaid order, which belongs in none of it
--
--   The mismatch is deliberately the 979 vs 978.57 case this feature was built for: a real
--   rounding difference, not a typo, and the row that proves the column works.
--
--   Enums are stored as strings in this schema (EF `HasConversion<string>()` throughout), which
--   is why the statuses below are words rather than ordinals.
--
-- GUARDS
--   Every row carries the tag, so it can be found and removed - see the tail of this file.
--   `demo_tag` is required and deliberately not defaulted: this writes rows that look like money.
--
-- USAGE
--   docker exec -i <pg-container> psql -U <user> -d <db> -v demo_tag=oct -f - < scripts/seed-demo-reports.sql
--
--   Then in the admin: Reports -> acquirer statement act, merchant Live, month = the previous one.

\set ON_ERROR_STOP on
\if :{?demo_tag}
\else
\echo 'demo_tag is required (see the header) - e.g. -v demo_tag=oct'
\quit
\endif

BEGIN;

-- The period and the tag, once, as a temp table rather than as psql variables: a substituted
-- variable inside a VALUES row is spliced as text, and because its neighbours are strings the
-- whole thing lands as a literal ("does not exist" at run time). A scalar subquery is always an
-- expression, so it survives being read as one value in a row constructor.
CREATE TEMP TABLE seed_params ON COMMIT DROP AS
SELECT date_trunc('month', now()) - interval '1 month' AS p_month,
       :'demo_tag'::text AS tag;

-- One account, so the reports describe a single coherent customer rather than a scatter of
-- strangers. Id derived from the tag, so re-running with the same tag does not duplicate.
INSERT INTO users (id, phone_number, is_active, created_at_utc, updated_at_utc)
SELECT md5('SEED-DEMO-' || tag || '-user')::uuid,
       '+380' || lpad((abs(hashtext('SEED-DEMO-' || tag)) % 100000000)::text, 8, '0'),
       true, now() - interval '40 days', now() - interval '40 days'
FROM seed_params
ON CONFLICT (id) DO NOTHING;

INSERT INTO orders (id, user_id, price, status, kind, monobank_invoice_id, monobank_status,
                    monobank_merchant, created_at_utc, updated_at_utc, fulfilled_at_utc)
SELECT v.id, md5('SEED-DEMO-' || p.tag || '-user')::uuid, v.price, v.status, v.kind,
       v.invoice_id, v.mono_status, v.mono_merchant,
       p.p_month + v.created_offset, now(),
       CASE WHEN v.fulfilled_offset IS NULL THEN NULL
            ELSE p.p_month + v.fulfilled_offset END
FROM seed_params p
JOIN LATERAL (VALUES
  -- Agrees exactly: the matcher must call this one Matched.
  (gen_random_uuid(), 1200.00::numeric, 'Paid'::text, 'Purchase'::text,
   'SEED-DEMO-INV-A-'      || p.tag, 'Success'::text, 'Live'::text,
   interval '2 days 10:00', interval '2 days 12:00'),
  -- The rounding disagreement: ours 979.00, the acquirer says 978.57.
  (gen_random_uuid(), 979.00::numeric, 'Paid'::text, 'Purchase'::text,
   'SEED-DEMO-INV-B-'      || p.tag, 'Success'::text, 'Live'::text,
   interval '5 days 9:30',  interval '5 days 11:00'),
  -- Paid, waiting to be fulfilled: the status the three-way match separates on.
  (gen_random_uuid(), 640.50::numeric, 'PendingFulfillment'::text, 'Purchase'::text,
   'SEED-DEMO-INV-C-'      || p.tag, 'Success'::text, 'Live'::text,
   interval '8 days 14:00', NULL),
  -- Paid, and the acquirer never mentions it: the "never mentioned" table.
  (gen_random_uuid(), 555.00::numeric, 'Fulfilled'::text, 'Purchase'::text,
   'SEED-DEMO-INV-SILENT-' || p.tag, 'Success'::text, 'Live'::text,
   interval '11 days 8:00', interval '11 days 8:30'),
  -- Never paid: belongs in no acquirer act, but the order counts and the funnel want it.
  (gen_random_uuid(), 300.00::numeric, 'PendingPayment'::text, 'Purchase'::text,
   NULL::text, NULL::text, NULL::text,
   interval '14 days 16:00', NULL)
) AS v(id, price, status, kind, invoice_id, mono_status, mono_merchant, created_offset, fulfilled_offset)
  ON true
WHERE NOT EXISTS (
  SELECT 1 FROM orders o
  WHERE (o.monobank_invoice_id IS NULL AND v.invoice_id IS NULL)
     OR o.monobank_invoice_id = v.invoice_id
);

-- A refund, so the "Refund match" verdict and the refunds report both have something in them.
INSERT INTO refunds (id, order_id, user_id, amount, invoice_id, ext_ref, status, is_automatic,
                     created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), o.id, o.user_id, 25000, 'SEED-DEMO-REF-' || p.tag,
       'SEED-DEMO-EXTREF-' || p.tag, 'Completed'::text, false,
       p.p_month + interval '20 days 10:00', now()
FROM seed_params p
JOIN orders o ON o.monobank_invoice_id = 'SEED-DEMO-INV-A-' || p.tag
WHERE NOT EXISTS (SELECT 1 FROM refunds r WHERE r.ext_ref = 'SEED-DEMO-EXTREF-' || p.tag);

-- The acquirer's side of the same month.
INSERT INTO monobank_statement_entries
  (id, merchant, statement_id, invoice_id, reference, amount_kopecks, ccy, status,
   acquirer_created_at_utc, acquirer_modified_at_utc, ext_ref, masked_card, rrn, approval_code,
   raw_payload, first_seen_at_utc, last_seen_at_utc)
SELECT v.id, 'Live'::text, v.statement_id, v.invoice_id, v.reference, v.kopecks, 980, 'settled'::text,
       p.p_month + v.at, p.p_month + v.at, v.ext_ref, v.card, v.rrn, v.approval,
       v.payload, now(), now()
FROM seed_params p
JOIN LATERAL (VALUES
  (gen_random_uuid(), 'SEED-DEMO-ST-1-' || p.tag,
   'SEED-DEMO-INV-A-' || p.tag, 'FuelFlow Order SEED-DEMO-' || p.tag,
   120000::bigint, interval '2 days 10:01', NULL::text, '5375********1234',
   '000000123', 'A1B2C3', '{"note":"agrees with our amount"}'::text),
  -- The row the whole feature exists for.
  (gen_random_uuid(), 'SEED-DEMO-ST-2-' || p.tag,
   'SEED-DEMO-INV-B-' || p.tag, 'FuelFlow Order SEED-DEMO-' || p.tag,
   97857::bigint, interval '5 days 9:31', NULL::text, '5375********5678',
   '000000124', 'D4E5F6', '{"note":"acquirer 978.57 vs our 979.00"}'::text),
  (gen_random_uuid(), 'SEED-DEMO-ST-3-' || p.tag,
   'SEED-DEMO-INV-C-' || p.tag, 'FuelFlow Order SEED-DEMO-' || p.tag,
   64050::bigint, interval '8 days 14:01', NULL::text, '5375********9012',
   '000000125', 'G7H8I9', '{"note":"paid, awaiting fulfillment"}'::text),
  -- Real money at the acquirer with nothing of ours behind it. Reported, never dropped.
  (gen_random_uuid(), 'SEED-DEMO-ST-4-' || p.tag,
   'SEED-DEMO-INV-ORPHAN-' || p.tag, 'FuelFlow Bundle SEED-DEMO-' || p.tag,
   50000::bigint, interval '12 days 13:00', NULL::text, '5375********3456',
   '000000126', 'J1K2L3', '{"note":"no order or refund of ours carries this invoice"}'::text),
  -- The acquirer reversing the refund above. Negative kopecks: money going back.
  (gen_random_uuid(), 'SEED-DEMO-ST-5-' || p.tag,
   'SEED-DEMO-REF-' || p.tag, 'FuelFlow Refund SEED-DEMO-' || p.tag,
   -25000::bigint, interval '20 days 10:05', 'SEED-DEMO-EXTREF-' || p.tag, '5375********1234',
   '000000127', 'M4N5O6', '{"note":"reversal of the seeded refund"}'::text)
) AS v(id, statement_id, invoice_id, reference, kopecks, at, ext_ref, card, rrn, approval, payload)
  ON true
WHERE NOT EXISTS (
  SELECT 1 FROM monobank_statement_entries e WHERE e.statement_id = v.statement_id
);

COMMIT;

-- What the act should now say. If these counts differ, the seed did not land as intended.
SELECT 'orders seeded'   AS check, count(*)::text AS value FROM orders
 WHERE monobank_invoice_id LIKE 'SEED-DEMO-%'
UNION ALL
SELECT 'refunds seeded', count(*)::text FROM refunds WHERE ext_ref LIKE 'SEED-DEMO-%'
UNION ALL
SELECT 'statement rows', count(*)::text FROM monobank_statement_entries WHERE statement_id LIKE 'SEED-DEMO-%';

\echo ''
\echo 'Reports -> acquirer statement act, merchant Live, month = the previous one.'
\echo 'Expect: 3 matched, 1 amount mismatch (978.57 vs 979.00), 1 unmatched, 1 refund match,'
\echo '        and one order in the "acquirer never mentioned" table.'
\echo ''
\echo 'Cleanup, with the same demo_tag:'
\echo '  DELETE FROM monobank_statement_entries WHERE statement_id LIKE ''SEED-DEMO-%-TAG%'';'
\echo '  DELETE FROM refunds                 WHERE ext_ref         LIKE ''SEED-DEMO-%-TAG%'';'
\echo '  DELETE FROM orders                  WHERE monobank_invoice_id LIKE ''SEED-DEMO-%-TAG%'';'
\echo '  DELETE FROM users                   WHERE id = md5(''SEED-DEMO-TAG-user'')::uuid;'