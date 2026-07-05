/**
 * Domain Repositories Barrel Export
 * 
 * Re-exports all repository interfaces for clean imports.
 */

// Base repository
export * from './base.repository';

// Entity repositories
export * from './user.repository';
export * from './order.repository';
export * from './voucher.repository';
export * from './fulfillment.repository';
export * from './outbox.repository';
export * from './station.repository';
export * from './package.repository';
export * from './fuel-type.repository';
export * from './qr-code.repository';
export * from './notification.repository';
export * from './import-job.repository';
export * from './phone-verification.repository';
