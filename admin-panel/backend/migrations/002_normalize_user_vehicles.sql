-- ============================================
-- Database Migration: Phase 2
-- Normalize User Vehicle Data
-- ============================================

-- Create user_vehicles table
CREATE TABLE IF NOT EXISTS user_vehicles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id VARCHAR NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    make VARCHAR,
    model VARCHAR,
    plate VARCHAR UNIQUE,
    fuel_type VARCHAR,
    is_primary BOOLEAN DEFAULT false,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

-- Create indexes
CREATE INDEX IF NOT EXISTS idx_user_vehicles_user ON user_vehicles(user_id);
CREATE INDEX IF NOT EXISTS idx_user_vehicles_plate ON user_vehicles(plate) WHERE plate IS NOT NULL;

-- Migrate existing vehicle data from users table
INSERT INTO user_vehicles (user_id, make, model, plate, fuel_type, is_primary, created_at)
SELECT 
    id,
    vehicle_make,
    vehicle_model,
    vehicle_plate,
    vehicle_fuel_type,
    true, -- Mark as primary vehicle
    created_at
FROM users
WHERE vehicle_make IS NOT NULL 
   OR vehicle_model IS NOT NULL 
   OR vehicle_plate IS NOT NULL 
   OR vehicle_fuel_type IS NOT NULL;

-- Remove vehicle columns from users table
-- (Commented out for safety - uncomment after verifying migration)
-- ALTER TABLE users 
--     DROP COLUMN IF EXISTS vehicle_make,
--     DROP COLUMN IF EXISTS vehicle_model,
--     DROP COLUMN IF EXISTS vehicle_plate,
--     DROP COLUMN IF EXISTS vehicle_fuel_type;

-- ============================================
-- Rollback Script (if needed)
-- ============================================

-- To rollback this migration:
-- 
-- -- Restore vehicle data to users table (if columns still exist)
-- UPDATE users u
-- SET 
--     vehicle_make = v.make,
--     vehicle_model = v.model,
--     vehicle_plate = v.plate,
--     vehicle_fuel_type = v.fuel_type
-- FROM user_vehicles v
-- WHERE u.id = v.user_id AND v.is_primary = true;
-- 
-- DROP INDEX IF EXISTS idx_user_vehicles_plate;
-- DROP INDEX IF EXISTS idx_user_vehicles_user;
-- DROP TABLE IF EXISTS user_vehicles;
