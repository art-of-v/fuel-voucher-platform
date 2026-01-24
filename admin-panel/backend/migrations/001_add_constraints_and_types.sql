-- ============================================
-- Database Migration: Phase 1
-- Add Constraints, ENUMs, and Improve Types
-- ============================================

-- Create ENUM types for status fields
CREATE TYPE voucher_status AS ENUM (
    'imported',
    'available',
    'reserved',
    'sold',
    'used',
    'expired'
);

CREATE TYPE order_status AS ENUM (
    'PENDING_FULFILLMENT',
    'FULFILLED',
    'REFUNDED',
    'CANCELLED'
);

CREATE TYPE qr_code_status AS ENUM (
    'available',
    'sold',
    'expired'
);

-- Add foreign key constraints
ALTER TABLE fuel_types 
    ADD CONSTRAINT fk_fuel_types_station 
    FOREIGN KEY (station_id) REFERENCES stations(id) 
    ON DELETE CASCADE;

ALTER TABLE fuel_packages 
    ADD CONSTRAINT fk_fuel_packages_station 
    FOREIGN KEY (station_id) REFERENCES stations(id) 
    ON DELETE CASCADE;

ALTER TABLE vouchers 
    ADD CONSTRAINT fk_vouchers_user 
    FOREIGN KEY (assigned_to_user_id) REFERENCES users(id) 
    ON DELETE SET NULL;

ALTER TABLE vouchers 
    ADD CONSTRAINT fk_vouchers_import_job 
    FOREIGN KEY (import_job_id) REFERENCES import_jobs(id) 
    ON DELETE SET NULL;

ALTER TABLE orders 
    ADD CONSTRAINT fk_orders_user 
    FOREIGN KEY (user_id) REFERENCES users(id) 
    ON DELETE CASCADE;

ALTER TABLE fulfillments 
    ADD CONSTRAINT fk_fulfillments_order 
    FOREIGN KEY (order_id) REFERENCES orders(id) 
    ON DELETE CASCADE;

ALTER TABLE fulfillments 
    ADD CONSTRAINT fk_fulfillments_voucher 
    FOREIGN KEY (voucher_id) REFERENCES vouchers(id) 
    ON DELETE CASCADE;

-- Improve data types
-- Convert lat/lng from TEXT to DECIMAL
ALTER TABLE stations 
    ALTER COLUMN lat TYPE DECIMAL(10, 8) USING lat::DECIMAL(10, 8),
    ALTER COLUMN lng TYPE DECIMAL(11, 8) USING lng::DECIMAL(11, 8);

-- Convert boolean fields from INTEGER to BOOLEAN
ALTER TABLE phone_verifications 
    ALTER COLUMN verified TYPE BOOLEAN USING (verified::INTEGER = 1);

ALTER TABLE notifications 
    ALTER COLUMN read TYPE BOOLEAN USING (read::INTEGER = 1);

ALTER TABLE outbox 
    ALTER COLUMN processed TYPE BOOLEAN USING (processed::INTEGER = 1);

-- Add performance indexes
CREATE INDEX IF NOT EXISTS idx_vouchers_provider_fuel ON vouchers(provider, fuel_type);
CREATE INDEX IF NOT EXISTS idx_vouchers_assigned_user ON vouchers(assigned_to_user_id) WHERE assigned_to_user_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_orders_user_status ON orders(user_id, status);
CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_fulfillments_composite ON fulfillments(order_id, voucher_id);
CREATE INDEX IF NOT EXISTS idx_phone_verifications_phone ON phone_verifications(phone);
CREATE INDEX IF NOT EXISTS idx_phone_verifications_expires ON phone_verifications(expires_at);
CREATE INDEX IF NOT EXISTS idx_notifications_user_read ON notifications(user_id, read);
CREATE INDEX IF NOT EXISTS idx_outbox_processed_created ON outbox(processed, created_at) WHERE processed = false;

-- Add unique constraints
CREATE UNIQUE INDEX IF NOT EXISTS idx_orders_idempotency ON orders(idempotency_key) WHERE idempotency_key IS NOT NULL;

-- ============================================
-- Rollback Script (if needed)
-- ============================================

-- To rollback this migration:
-- 
-- DROP INDEX IF EXISTS idx_outbox_processed_created;
-- DROP INDEX IF EXISTS idx_notifications_user_read;
-- DROP INDEX IF EXISTS idx_phone_verifications_expires;
-- DROP INDEX IF EXISTS idx_phone_verifications_phone;
-- DROP INDEX IF EXISTS idx_fulfillments_composite;
-- DROP INDEX IF EXISTS idx_orders_created_at;
-- DROP INDEX IF EXISTS idx_orders_user_status;
-- DROP INDEX IF EXISTS idx_vouchers_assigned_user;
-- DROP INDEX IF EXISTS idx_vouchers_provider_fuel;
-- DROP INDEX IF EXISTS idx_orders_idempotency;
-- 
-- ALTER TABLE stations 
--     ALTER COLUMN lat TYPE TEXT,
--     ALTER COLUMN lng TYPE TEXT;
-- 
-- ALTER TABLE phone_verifications 
--     ALTER COLUMN verified TYPE INTEGER USING (CASE WHEN verified THEN 1 ELSE 0 END);
-- 
-- ALTER TABLE notifications 
--     ALTER COLUMN read TYPE INTEGER USING (CASE WHEN read THEN 1 ELSE 0 END);
-- 
-- ALTER TABLE outbox 
--     ALTER COLUMN processed TYPE INTEGER USING (CASE WHEN processed THEN 1 ELSE 0 END);
-- 
-- ALTER TABLE fulfillments DROP CONSTRAINT IF EXISTS fk_fulfillments_voucher;
-- ALTER TABLE fulfillments DROP CONSTRAINT IF EXISTS fk_fulfillments_order;
-- ALTER TABLE orders DROP CONSTRAINT IF EXISTS fk_orders_user;
-- ALTER TABLE vouchers DROP CONSTRAINT IF EXISTS fk_vouchers_import_job;
-- ALTER TABLE vouchers DROP CONSTRAINT IF EXISTS fk_vouchers_user;
-- ALTER TABLE fuel_packages DROP CONSTRAINT IF EXISTS fk_fuel_packages_station;
-- ALTER TABLE fuel_types DROP CONSTRAINT IF EXISTS fk_fuel_types_station;
-- 
-- DROP TYPE IF EXISTS qr_code_status;
-- DROP TYPE IF EXISTS order_status;
-- DROP TYPE IF EXISTS voucher_status;
