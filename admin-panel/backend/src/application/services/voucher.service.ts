/**
 * Voucher Service
 * 
 * Handles voucher management and queries.
 */

import {
    IVoucherRepository,
    Voucher,
    VoucherFilterOptions,
    VoucherQueryResult,
    UserVoucher,
    InventoryItem
} from '../../domain/repositories/voucher.repository';
import { PaginationOptions, SortOptions } from '../../domain/repositories/base.repository';
import { logger } from '../../infrastructure/logging/logger';

export class VoucherService {
    private readonly log = logger.child({ component: 'VoucherService' });

    constructor(
        private readonly voucherRepository: IVoucherRepository,
    ) { }

    /**
     * Get vouchers with filters
     */
    async getVouchers(
        filters?: VoucherFilterOptions,
        pagination?: PaginationOptions,
        sort?: SortOptions,
    ): Promise<VoucherQueryResult> {
        return this.voucherRepository.findWithFilters(filters, pagination, sort);
    }

    /**
     * Get voucher by ID
     */
    async getVoucherById(id: string): Promise<Voucher | null> {
        return this.voucherRepository.findById(id);
    }

    /**
     * Get user's vouchers
     */
    async getUserVouchers(userId: string): Promise<UserVoucher[]> {
        return this.voucherRepository.findByUserId(userId);
    }

    /**
     * Get inventory aggregation
     */
    async getInventory(): Promise<InventoryItem[]> {
        return this.voucherRepository.getInventoryAggregation();
    }

    /**
     * Update voucher
     */
    async updateVoucher(id: string, data: Partial<Voucher>): Promise<Voucher> {
        return this.voucherRepository.update(id, data);
    }

    /**
     * Delete voucher
     */
    async deleteVoucher(id: string): Promise<void> {
        await this.voucherRepository.delete(id);
        this.log.info({ voucherId: id }, 'Voucher deleted');
    }

    /**
     * Delete all vouchers (admin only)
     */
    async deleteAllVouchers(): Promise<void> {
        await this.voucherRepository.deleteAll();
        this.log.warn('All vouchers deleted');
    }

    /**
     * Manual voucher assignment (test endpoint)
     */
    async assignVouchersManually(
        userId: string,
        items: Array<{ station: string; fuelType: string; liters: number; quantity: number }>
    ): Promise<Array<{ success: boolean; item: any; assigned?: number; error?: string }>> {
        const results = [];

        for (const item of items) {
            this.log.info({ userId, item }, 'Manually assigning vouchers');

            try {
                const assigned = await this.voucherRepository.assignToPurchase(
                    0, // No purchase ID for manual assignment
                    userId,
                    item.station,
                    item.fuelType,
                    item.liters,
                    item.quantity
                );

                results.push({ success: true, item, assigned: assigned.length });
                this.log.info({ assigned: assigned.length }, 'Successfully assigned vouchers');
            } catch (error: any) {
                this.log.error({ error: error.message }, 'Failed to assign vouchers');
                results.push({ success: false, item, error: error.message });
            }
        }

        return results;
    }
}
