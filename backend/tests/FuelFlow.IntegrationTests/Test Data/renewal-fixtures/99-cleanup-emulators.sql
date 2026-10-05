-- =============================================================================================
-- Campaign cleanup — removes exactly what the emulator fixtures created
--
-- Safe by construction: it only ever touches rows whose voucher_number starts with `EMU2-` or whose
-- package id starts with `emu2-pkg-`. A customer's own vouchers cannot match, so this needs no
-- allowlist from the operator and no judgement call at 2am.
--
-- Order matters for the same reason as the audit: unhook, then delete.
--
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 < 99-cleanup-emulators.sql
-- =============================================================================================

\set ON_ERROR_STOP on

BEGIN;

CREATE TEMP TABLE _emu AS
SELECT id FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%';

DELETE FROM fulfillments WHERE voucher_id IN (SELECT id FROM _emu);
DELETE FROM voucher_renewal_items
WHERE source_voucher_id IN (SELECT id FROM _emu)
   OR fulfilled_voucher_id IN (SELECT id FROM _emu);

UPDATE voucher_exchanges SET new_voucher_id = NULL
WHERE new_voucher_id IN (SELECT id FROM _emu);
DELETE FROM voucher_exchanges WHERE old_voucher_id IN (SELECT id FROM _emu);

DELETE FROM operator_voucher_renewals WHERE voucher_id IN (SELECT id FROM _emu);
UPDATE fuel_vouchers SET order_id = NULL WHERE id IN (SELECT id FROM _emu);
DELETE FROM fuel_vouchers WHERE id IN (SELECT id FROM _emu);

DELETE FROM fuel_packages WHERE id LIKE 'emu2-pkg-%';

COMMIT;

\echo '=== remaining emulator rows (must all be 0) ==='
SELECT
  (SELECT count(*) FROM fuel_vouchers WHERE voucher_number LIKE 'EMU2-%') AS emu_vouchers,
  (SELECT count(*) FROM fuel_packages WHERE id LIKE 'emu2-pkg-%') AS emu_packages;