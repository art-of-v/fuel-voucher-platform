/**
 * Voucher Repository Interface
 * 
 * Defines persistence operations for Voucher entities.
 */

import { IBaseRepository, PaginationOptions, SortOptions } from './base.repository';

/**
 * Voucher status enum
 */
export type VoucherStatus =
    | 'imported'
    | 'available'
    | 'reserved'
    | 'sold'
    | 'used'
    | 'expired';

/**
 * Voucher entity (domain representation)
 */
export interface Voucher {
    id: string;
    provider: string;
    externalId: string | null;
    fuelType: string;
    fuelSubtype: string | null;
    amount: number;
    unit: string;
    expirationDate: Date | null;
    status: VoucherStatus;
    redemptionRules: string | null;
    imageUrl: string | null;
    qrCodeData: string | null;
    originalFileName: string | null;
    source: string;
    importJobId: string | null;
    assignedToUserId: string | null;
    purchaseId: number | null;
    createdAt: Date;
    updatedAt: Date;
}

/**
 * Voucher creation data
 */
export interface CreateVoucherData {
    provider: string;
    externalId?: string;
    fuelType: string;
    fuelSubtype?: string;
    amount: number;
    unit?: string;
    expirationDate?: Date;
    status?: VoucherStatus;
    redemptionRules?: string;
    qrCodeData?: string;
    originalFileName?: string;
    source: string;
    importJobId?: string;
}

/**
 * Voucher filter options
 */
export interface VoucherFilterOptions {
    status?: VoucherStatus;
    provider?: string;
    fuelType?: string;
    assignedToUserId?: string;
}

/**
 * Voucher query result
 */
export interface VoucherQueryResult {
    data: Voucher[];
    total: number;
    globalTotal: number;
    fuelTypes: string[];
}

/**
 * User voucher (simplified for API)
 */
export interface UserVoucher {
    id: string;
    provider: string;
    externalId: string | null;
    fuelType: string;
    amount: number;
    status: VoucherStatus;
    unit: string;
    qrCodeData: string | null;
    qrCodeUrl: string | null;
    imageUrl: string | null;
}

/**
 * Inventory aggregation
 */
export interface InventoryItem {
    provider: string;
    fuelType: string;
    liters: number;
    availableCount: number;
}

/**
 * Voucher Repository Interface
 */
export interface IVoucherRepository extends IBaseRepository<Voucher, string> {
    /**
     * Get voucher by external ID
     */
    findByExternalId(provider: string, externalId: string): Promise<Voucher | null>;

    /**
     * Query vouchers with filters and pagination
     */
    findWithFilters(
        filters?: VoucherFilterOptions,
        pagination?: PaginationOptions,
        sort?: SortOptions,
    ): Promise<VoucherQueryResult>;

    /**
     * Get vouchers assigned to a user
     */
    findByUserId(userId: string): Promise<UserVoucher[]>;

    /**
     * Get inventory aggregation
     */
    getInventoryAggregation(): Promise<InventoryItem[]>;

    /**
     * Find available voucher matching criteria
     */
    findAvailable(
        provider: string,
        fuelType: string,
        liters: number,
    ): Promise<Voucher | null>;

    /**
     * Assign vouchers to a purchase (transactional)
     */
    assignToPurchase(
        purchaseId: number,
        userId: string,
        provider: string,
        fuelType: string,
        liters: number,
        quantity: number,
    ): Promise<Voucher[]>;

    /**
     * Update voucher
     */
    update(id: string, data: Partial<Voucher>): Promise<Voucher>;

    /**
     * Delete all vouchers (admin only)
     */
    deleteAll(): Promise<void>;

    /**
     * Create voucher (with idempotency check)
     */
    create(data: CreateVoucherData, throwOnDuplicate?: boolean): Promise<Voucher>;
}
