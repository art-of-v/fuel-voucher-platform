# Database Schema Migration Specification

## Overview

This document details the database schema changes required for the architectural refactoring. All migrations are designed to be:
- **Non-breaking**: Existing data is preserved
- **Reversible**: Each migration has a rollback
- **Incremental**: Applied in phases

---

## Current Schema Summary

### Existing Tables (14 total)

| Table | Primary Key | Row Count (Est.) | Status |
|-------|-------------|------------------|--------|
| `users` | `id` (VARCHAR) | Low | **Keep + Enhance** |
| `sessions` | `sid` (VARCHAR) | Medium | Keep |
| `phone_verifications` | `id` (SERIAL) | Medium | **Modify** |
| `stations` | `id` (TEXT) | Low | Keep |
| `fuel_types` | `id` (TEXT) | Low | Keep |
| `fuel_packages` | `id` (TEXT) | Low | Keep |
| `qr_codes` | `id` (SERIAL) | Low-Medium | **Deprecate** |
| `purchases` | `id` (SERIAL) | Medium | **Soft-Deprecate** |
| `notifications` | `id` (SERIAL) | Medium | Keep |
| `import_jobs` | `id` (UUID) | Low-Medium | **Enhance** |
| `vouchers` | `id` (UUID) | High | **Enhance** |
| `orders` | `id` (UUID) | Medium | **Enhance** |
| `fulfillments` | `id` (SERIAL) | Medium | Keep |
| `outbox` | `id` (SERIAL) | Medium | **Enhance** |

---

## Phase 1: Add Enumerated Types

### Migration: 001_create_enums.sql

```sql
-- Create enumerated types for constrained values
-- This improves data integrity and query performance

-- Voucher status enum
DO $$ BEGIN
    CREATE TYPE voucher_status AS ENUM (
        'imported',      -- Just imported from PDF
        'available',     -- Ready for assignment
        'reserved',      -- Temporarily held (future: cart reservation)
        'sold',          -- Assigned to user
        'used',          -- Redeemed at station
        'expired',       -- Past expiration date
        'revoked'        -- Manually invalidated
    );
EXCEPTION
    WHEN duplicate_object THEN null;
END $$;

-- Order status enum
DO $$ BEGIN
    CREATE TYPE order_status AS ENUM (
        'PENDING_PAYMENT',      -- Awaiting payment
        'PENDING_FULFILLMENT',  -- Payment received, awaiting voucher
        'PARTIALLY_FULFILLED',  -- Some vouchers assigned
        'FULFILLED',            -- All vouchers assigned
        'REFUNDED',             -- Money returned
        'CANCELLED'             -- Order cancelled
    );
EXCEPTION
    WHEN duplicate_object THEN null;
END $$;

-- Payment method enum
DO $$ BEGIN
    CREATE TYPE payment_method AS ENUM (
        'stripe',
        'apple_pay',
        'google_pay',
        'bonus_balance',
        'free'   -- Promotional
    );
EXCEPTION
    WHEN duplicate_object THEN null;
END $$;
```

### Rollback: 001_create_enums_down.sql

```sql
-- Cannot drop enum if in use, so this is conditional
DROP TYPE IF EXISTS voucher_status CASCADE;
DROP TYPE IF EXISTS order_status CASCADE;
DROP TYPE IF EXISTS payment_method CASCADE;
```

---

## Phase 2: Enhance Users Table

### Migration: 002_enhance_users.sql

```sql
-- Add audit columns and normalize ID type
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS phone_verified BOOLEAN DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS email_verified BOOLEAN DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS created_by VARCHAR(255),
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR(255),
    ADD COLUMN IF NOT EXISTS last_login_at TIMESTAMP,
    ADD COLUMN IF NOT EXISTS login_count INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS status VARCHAR(20) DEFAULT 'active';

-- Create index for phone lookups (if not exists)
CREATE INDEX IF NOT EXISTS idx_users_phone ON users(phone) WHERE phone IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email) WHERE email IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_users_status ON users(status);

-- Add check constraint for status
ALTER TABLE users DROP CONSTRAINT IF EXISTS chk_users_status;
ALTER TABLE users ADD CONSTRAINT chk_users_status 
    CHECK (status IN ('active', 'inactive', 'suspended', 'deleted'));

COMMENT ON COLUMN users.phone_verified IS 'True if phone was verified via OTP';
COMMENT ON COLUMN users.status IS 'Account status: active, inactive, suspended, deleted';
```

### Rollback: 002_enhance_users_down.sql

```sql
ALTER TABLE users
    DROP COLUMN IF EXISTS phone_verified,
    DROP COLUMN IF EXISTS email_verified,
    DROP COLUMN IF EXISTS created_by,
    DROP COLUMN IF EXISTS updated_by,
    DROP COLUMN IF EXISTS last_login_at,
    DROP COLUMN IF EXISTS login_count,
    DROP COLUMN IF EXISTS status;

DROP INDEX IF EXISTS idx_users_phone;
DROP INDEX IF EXISTS idx_users_email;
DROP INDEX IF EXISTS idx_users_status;
```

---

## Phase 3: Enhance Vouchers Table

### Migration: 003_enhance_vouchers.sql

```sql
-- Add FK constraint for user assignment (deferred to allow NULL)
-- First, ensure all existing assigned_to_user_id values are valid
UPDATE vouchers v
SET assigned_to_user_id = NULL
WHERE assigned_to_user_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM users u WHERE u.id = v.assigned_to_user_id);

-- Add audit and tracking columns
ALTER TABLE vouchers
    ADD COLUMN IF NOT EXISTS assigned_at TIMESTAMP,
    ADD COLUMN IF NOT EXISTS used_at TIMESTAMP,
    ADD COLUMN IF NOT EXISTS created_by VARCHAR(255),
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR(255),
    ADD COLUMN IF NOT EXISTS source_page INTEGER,  -- Which page of PDF
    ADD COLUMN IF NOT EXISTS currency_code VARCHAR(3) DEFAULT 'UAH';

-- Backfill assigned_at for existing sold vouchers
UPDATE vouchers 
SET assigned_at = updated_at 
WHERE status = 'sold' AND assigned_at IS NULL;

-- Create composite index for fulfillment queries
CREATE INDEX IF NOT EXISTS idx_vouchers_available 
    ON vouchers(provider, fuel_type, amount, status) 
    WHERE status IN ('imported', 'available');

CREATE INDEX IF NOT EXISTS idx_vouchers_user_assignment 
    ON vouchers(assigned_to_user_id, status) 
    WHERE assigned_to_user_id IS NOT NULL;

-- Note: FK constraint added in Phase 6 after data cleanup
COMMENT ON COLUMN vouchers.source_page IS 'Page number in source PDF (for multi-page imports)';
COMMENT ON COLUMN vouchers.assigned_at IS 'Timestamp when voucher was assigned to user';
```

### Rollback: 003_enhance_vouchers_down.sql

```sql
ALTER TABLE vouchers
    DROP COLUMN IF EXISTS assigned_at,
    DROP COLUMN IF EXISTS used_at,
    DROP COLUMN IF EXISTS created_by,
    DROP COLUMN IF EXISTS updated_by,
    DROP COLUMN IF EXISTS source_page,
    DROP COLUMN IF EXISTS currency_code;

DROP INDEX IF EXISTS idx_vouchers_available;
DROP INDEX IF EXISTS idx_vouchers_user_assignment;
```

---

## Phase 4: Enhance Orders Table

### Migration: 004_enhance_orders.sql

```sql
-- Add FK columns and monetary improvements
ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS unit_price_minor INTEGER,
    ADD COLUMN IF NOT EXISTS total_price_minor INTEGER,
    ADD COLUMN IF NOT EXISTS currency_code VARCHAR(3) DEFAULT 'UAH',
    ADD COLUMN IF NOT EXISTS payment_method_type VARCHAR(20),
    ADD COLUMN IF NOT EXISTS created_by VARCHAR(255),
    ADD COLUMN IF NOT EXISTS updated_by VARCHAR(255),
    ADD COLUMN IF NOT EXISTS cancelled_at TIMESTAMP,
    ADD COLUMN IF NOT EXISTS cancellation_reason TEXT;

-- Backfill price columns
UPDATE orders 
SET 
    unit_price_minor = price / NULLIF(quantity, 0),
    total_price_minor = price
WHERE unit_price_minor IS NULL;

-- Fix: user_id FK validation
UPDATE orders o
SET user_id = (SELECT id FROM users LIMIT 1)
WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.id = o.user_id);

-- Create indexes for common queries
CREATE INDEX IF NOT EXISTS idx_orders_user_status 
    ON orders(user_id, status);

CREATE INDEX IF NOT EXISTS idx_orders_pending_product 
    ON orders(provider, fuel_type, liters, status) 
    WHERE status = 'PENDING_FULFILLMENT';

CREATE INDEX IF NOT EXISTS idx_orders_created_desc 
    ON orders(created_at DESC);
```

### Rollback: 004_enhance_orders_down.sql

```sql
ALTER TABLE orders
    DROP COLUMN IF EXISTS unit_price_minor,
    DROP COLUMN IF EXISTS total_price_minor,
    DROP COLUMN IF EXISTS currency_code,
    DROP COLUMN IF EXISTS payment_method_type,
    DROP COLUMN IF EXISTS created_by,
    DROP COLUMN IF EXISTS updated_by,
    DROP COLUMN IF EXISTS cancelled_at,
    DROP COLUMN IF EXISTS cancellation_reason;

DROP INDEX IF EXISTS idx_orders_user_status;
DROP INDEX IF EXISTS idx_orders_pending_product;
DROP INDEX IF EXISTS idx_orders_created_desc;
```

---

## Phase 5: Enhance Outbox Table

### Migration: 005_enhance_outbox.sql

```sql
-- Improve outbox for reliable event processing
ALTER TABLE outbox
    ADD COLUMN IF NOT EXISTS aggregate_type VARCHAR(50),
    ADD COLUMN IF NOT EXISTS aggregate_id VARCHAR(100),
    ADD COLUMN IF NOT EXISTS attempts INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_error TEXT,
    ADD COLUMN IF NOT EXISTS scheduled_for TIMESTAMP DEFAULT NOW(),
    ADD COLUMN IF NOT EXISTS correlation_id VARCHAR(100);

-- Backfill aggregate info from payload
UPDATE outbox
SET 
    aggregate_type = CASE 
        WHEN event_type LIKE 'ORDER%' THEN 'Order'
        WHEN event_type LIKE 'VOUCHER%' THEN 'Voucher'
        ELSE 'Unknown'
    END,
    aggregate_id = COALESCE(
        (payload->>'orderId')::text,
        (payload->>'voucherId')::text,
        'unknown-' || id::text
    )
WHERE aggregate_type IS NULL;

-- Create index for polling
DROP INDEX IF EXISTS idx_outbox_processed;
CREATE INDEX idx_outbox_pending 
    ON outbox(scheduled_for, attempts) 
    WHERE processed = 0;

-- Convert processed to boolean (new column, then swap)
ALTER TABLE outbox ADD COLUMN IF NOT EXISTS is_processed BOOLEAN DEFAULT FALSE;
UPDATE outbox SET is_processed = (processed = 1);

COMMENT ON COLUMN outbox.aggregate_type IS 'Type of aggregate this event relates to (Order, Voucher, etc.)';
COMMENT ON COLUMN outbox.aggregate_id IS 'ID of the aggregate for correlation';
COMMENT ON COLUMN outbox.attempts IS 'Number of processing attempts';
COMMENT ON COLUMN outbox.scheduled_for IS 'When to process (for delayed events)';
```

### Rollback: 005_enhance_outbox_down.sql

```sql
ALTER TABLE outbox
    DROP COLUMN IF EXISTS aggregate_type,
    DROP COLUMN IF EXISTS aggregate_id,
    DROP COLUMN IF EXISTS attempts,
    DROP COLUMN IF EXISTS last_error,
    DROP COLUMN IF EXISTS scheduled_for,
    DROP COLUMN IF EXISTS correlation_id,
    DROP COLUMN IF EXISTS is_processed;

CREATE INDEX IF NOT EXISTS idx_outbox_processed 
    ON outbox(processed) WHERE processed = 0;
```

---

## Phase 6: Create Fuel Type Aliases Table

### Migration: 006_create_fuel_aliases.sql

```sql
-- Move fuel type matching logic from code to database
CREATE TABLE IF NOT EXISTS fuel_type_aliases (
    id SERIAL PRIMARY KEY,
    canonical_name VARCHAR(50) NOT NULL,
    alias VARCHAR(100) NOT NULL,
    locale VARCHAR(5) DEFAULT 'uk',
    provider VARCHAR(50),  -- NULL means applies to all
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(alias, provider)
);

-- Seed with existing aliases from getFuelAliases()
INSERT INTO fuel_type_aliases (canonical_name, alias, locale, provider) VALUES
    -- Diesel variants
    ('DIESEL', 'Diesel', 'en', NULL),
    ('DIESEL', 'ДП', 'uk', NULL),
    ('DIESEL', 'ДП ЄВРО', 'uk', NULL),
    ('DIESEL', 'ДП ЕВРО', 'ru', NULL),
    ('DIESEL', 'DP', 'en', NULL),
    ('DIESEL', 'diesel', 'en', NULL),
    ('DIESEL', 'Diesel Mustang', 'en', NULL),
    ('DIESEL', 'ДП Mustang', 'uk', NULL),
    
    -- A-95 variants
    ('A95', 'A-95', 'en', NULL),
    ('A95', 'А-95', 'uk', NULL),
    ('A95', 'A-95 ЄВРО', 'uk', NULL),
    ('A95', 'А-95 ЄВРО', 'uk', NULL),
    ('A95', 'A-95 ЕВРО', 'ru', NULL),
    ('A95', 'А-95 ЕВРО', 'ru', NULL),
    ('A95', '95', 'en', NULL),
    ('A95', 'a-95', 'en', NULL),
    ('A95', 'A-95 Pulls', 'en', NULL),
    ('A95', 'Pulls 95', 'en', NULL),
    ('A95', 'A-95 Mustang', 'en', NULL),
    ('A95', 'Mustang 95', 'en', NULL),
    
    -- Premium variants
    ('UPG100', 'UPG-100', 'en', NULL),
    ('UPG100', '100', 'en', NULL)
ON CONFLICT (alias, provider) DO NOTHING;

CREATE INDEX idx_fuel_alias_lookup ON fuel_type_aliases(alias);
CREATE INDEX idx_fuel_alias_canonical ON fuel_type_aliases(canonical_name);

COMMENT ON TABLE fuel_type_aliases IS 'Mapping of fuel type labels to canonical names for matching';
```

### Rollback: 006_create_fuel_aliases_down.sql

```sql
DROP TABLE IF EXISTS fuel_type_aliases;
```

---

## Phase 7: Add Foreign Key Constraints

### Migration: 007_add_foreign_keys.sql

```sql
-- Add FK constraints after data is validated/cleaned

-- Orders -> Users
ALTER TABLE orders 
    ADD CONSTRAINT fk_orders_user 
    FOREIGN KEY (user_id) REFERENCES users(id) 
    ON DELETE RESTRICT;

-- Vouchers -> Users (optional FK, can be NULL)
-- Only if data is clean
DO $$
BEGIN
    -- Check if any orphaned assignments exist
    IF NOT EXISTS (
        SELECT 1 FROM vouchers v 
        WHERE v.assigned_to_user_id IS NOT NULL 
        AND NOT EXISTS (SELECT 1 FROM users u WHERE u.id = v.assigned_to_user_id)
    ) THEN
        ALTER TABLE vouchers 
            ADD CONSTRAINT fk_vouchers_assigned_user 
            FOREIGN KEY (assigned_to_user_id) REFERENCES users(id) 
            ON DELETE SET NULL;
    ELSE
        RAISE NOTICE 'Skipping vouchers FK: orphaned records exist. Clean up first.';
    END IF;
END $$;

-- Notifications -> Users
ALTER TABLE notifications 
    ADD CONSTRAINT fk_notifications_user 
    FOREIGN KEY (user_id) REFERENCES users(id) 
    ON DELETE CASCADE;

-- Import Jobs audit
ALTER TABLE import_jobs
    ADD CONSTRAINT fk_import_jobs_admin
    FOREIGN KEY (admin_id) REFERENCES users(id)
    ON DELETE SET NULL;
```

### Rollback: 007_add_foreign_keys_down.sql

```sql
ALTER TABLE orders DROP CONSTRAINT IF EXISTS fk_orders_user;
ALTER TABLE vouchers DROP CONSTRAINT IF EXISTS fk_vouchers_assigned_user;
ALTER TABLE notifications DROP CONSTRAINT IF EXISTS fk_notifications_user;
ALTER TABLE import_jobs DROP CONSTRAINT IF EXISTS fk_import_jobs_admin;
```

---

## Phase 8: Deprecation Views (Non-Breaking)

### Migration: 008_create_compatibility_views.sql

```sql
-- Create views for backwards compatibility during transition

-- Legacy purchases view
CREATE OR REPLACE VIEW v_legacy_purchases AS
SELECT 
    o.id::integer as id,  -- Approximate legacy ID
    o.user_id as session_id,
    o.product_type as package_id,
    o.provider as station_id,
    o.provider as station_name,
    o.fuel_type as fuel_type,
    o.fuel_type as fuel_name,
    o.liters,
    o.price,
    NULL::integer as qr_code_id,
    (SELECT id FROM vouchers v JOIN fulfillments f ON f.voucher_id = v.id WHERE f.order_id = o.id LIMIT 1) as voucher_id,
    CASE 
        WHEN o.status = 'FULFILLED' THEN 'delivered'
        WHEN o.status = 'PENDING_FULFILLMENT' THEN 'pending'
        ELSE 'pending'
    END as status,
    o.stripe_payment_id as stripe_session_id,
    o.created_at
FROM orders o;

COMMENT ON VIEW v_legacy_purchases IS 'Backwards-compatible view mimicking old purchases table. Use orders table for new code.';

-- Inventory aggregation view
CREATE OR REPLACE VIEW v_inventory_summary AS
SELECT 
    provider,
    fuel_type,
    amount as liters,
    COUNT(*) as available_count
FROM vouchers
WHERE status IN ('imported', 'available')
GROUP BY provider, fuel_type, amount;
```

---

## Schema Mapping: Old → New

| Old Column/Table | New Location | Notes |
|------------------|--------------|-------|
| `purchases.session_id` | `orders.user_id` | Renamed for clarity |
| `purchases.qr_code_id` | Removed | Use `fulfillments` join |
| `purchases.voucher_id` | `fulfillments.voucher_id` | Proper join table |
| `vouchers.status` (TEXT) | `vouchers.status` (ENUM) | Type constraint added |
| `outbox.processed` (INT) | `outbox.is_processed` (BOOL) | Proper boolean |
| Hard-coded fuel aliases | `fuel_type_aliases` table | Configurable |

---

## Execution Order

```
1. 001_create_enums.sql
2. 002_enhance_users.sql
3. 003_enhance_vouchers.sql
4. 004_enhance_orders.sql
5. 005_enhance_outbox.sql
6. 006_create_fuel_aliases.sql
7. [Manual: Data cleanup scripts]
8. 007_add_foreign_keys.sql
9. 008_create_compatibility_views.sql
```

---

## Verification Queries

After migration, run these to verify data integrity:

```sql
-- Check all FKs are valid
SELECT 'orders_user' as fk, COUNT(*) as orphans
FROM orders o 
WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.id = o.user_id)

UNION ALL

SELECT 'vouchers_user', COUNT(*)
FROM vouchers v 
WHERE v.assigned_to_user_id IS NOT NULL 
AND NOT EXISTS (SELECT 1 FROM users u WHERE u.id = v.assigned_to_user_id)

UNION ALL

SELECT 'fulfillments_order', COUNT(*)
FROM fulfillments f 
WHERE NOT EXISTS (SELECT 1 FROM orders o WHERE o.id = f.order_id)

UNION ALL

SELECT 'fulfillments_voucher', COUNT(*)
FROM fulfillments f 
WHERE NOT EXISTS (SELECT 1 FROM vouchers v WHERE v.id = f.voucher_id);

-- Expected: All zeros
```
