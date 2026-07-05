-- ============================================
-- Database Migration: Phase 3
-- Add Audit Columns
-- ============================================

-- Add audit columns to key tables
ALTER TABLE vouchers 
    ADD COLUMN IF NOT EXISTS created_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR,
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP;

ALTER TABLE orders 
    ADD COLUMN IF NOT EXISTS created_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR,
    ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMP;

ALTER TABLE import_jobs 
    ADD COLUMN IF NOT EXISTS created_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR;

ALTER TABLE stations 
    ADD COLUMN IF NOT EXISTS created_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP DEFAULT NOW();

ALTER TABLE fuel_packages 
    ADD COLUMN IF NOT EXISTS created_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP DEFAULT NOW();

ALTER TABLE fuel_types 
    ADD COLUMN IF NOT EXISTS created_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP DEFAULT NOW();

-- Create trigger function for auto-updating updated_at
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Apply triggers to tables with updated_at
CREATE TRIGGER trigger_vouchers_updated_at
    BEFORE UPDATE ON vouchers
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER trigger_stations_updated_at
    BEFORE UPDATE ON stations
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER trigger_fuel_packages_updated_at
    BEFORE UPDATE ON fuel_packages
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER trigger_fuel_types_updated_at
    BEFORE UPDATE ON fuel_types
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER trigger_import_jobs_updated_at
    BEFORE UPDATE ON import_jobs
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Create indexes for soft deletes
CREATE INDEX IF NOT EXISTS idx_vouchers_deleted_at ON vouchers(deleted_at) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_orders_deleted_at ON orders(deleted_at) WHERE deleted_at IS NULL;

-- ============================================
-- Rollback Script (if needed)
-- ============================================

-- To rollback this migration:
-- 
-- DROP INDEX IF EXISTS idx_orders_deleted_at;
-- DROP INDEX IF EXISTS idx_vouchers_deleted_at;
-- 
-- DROP TRIGGER IF EXISTS trigger_import_jobs_updated_at ON import_jobs;
-- DROP TRIGGER IF EXISTS trigger_fuel_types_updated_at ON fuel_types;
-- DROP TRIGGER IF EXISTS trigger_fuel_packages_updated_at ON fuel_packages;
-- DROP TRIGGER IF EXISTS trigger_stations_updated_at ON stations;
-- DROP TRIGGER IF EXISTS trigger_vouchers_updated_at ON vouchers;
-- 
-- DROP FUNCTION IF EXISTS update_updated_at_column();
-- 
-- ALTER TABLE fuel_types DROP COLUMN IF EXISTS updated_by, DROP COLUMN IF EXISTS created_by, DROP COLUMN IF EXISTS updated_at;
-- ALTER TABLE fuel_packages DROP COLUMN IF EXISTS updated_by, DROP COLUMN IF EXISTS created_by, DROP COLUMN IF EXISTS updated_at;
-- ALTER TABLE stations DROP COLUMN IF EXISTS updated_by, DROP COLUMN IF EXISTS created_by, DROP COLUMN IF EXISTS updated_at;
-- ALTER TABLE import_jobs DROP COLUMN IF EXISTS updated_by, DROP COLUMN IF EXISTS created_by;
-- ALTER TABLE orders DROP COLUMN IF EXISTS deleted_at, DROP COLUMN IF EXISTS updated_by, DROP COLUMN IF EXISTS created_by;
-- ALTER TABLE vouchers DROP COLUMN IF EXISTS deleted_at, DROP COLUMN IF EXISTS updated_by, DROP COLUMN IF EXISTS created_by;
