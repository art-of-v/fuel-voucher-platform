
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { VoucherService } from '../../src/application/services/voucher.service';

describe('Voucher Service', () => {
    let voucherService: VoucherService;
    const mockVoucherRepository = {
        findWithFilters: vi.fn(),
        findById: vi.fn(),
        findByUserId: vi.fn(),
        getInventoryAggregation: vi.fn(),
        update: vi.fn(),
        delete: vi.fn(),
        deleteAll: vi.fn(),
        assignToPurchase: vi.fn(),
    };

    beforeEach(() => {
        vi.clearAllMocks();
        voucherService = new VoucherService(mockVoucherRepository as any);
    });

    describe('Pass-through methods', () => {
        it('should get vouchers', async () => {
            const filters = { status: 'available' } as any;
            await voucherService.getVouchers(filters);
            expect(mockVoucherRepository.findWithFilters).toHaveBeenCalledWith(filters, undefined, undefined);
        });

        it('should get voucher by id', async () => {
            await voucherService.getVoucherById('v1');
            expect(mockVoucherRepository.findById).toHaveBeenCalledWith('v1');
        });

        it('should get user vouchers', async () => {
            await voucherService.getUserVouchers('u1');
            expect(mockVoucherRepository.findByUserId).toHaveBeenCalledWith('u1');
        });

        it('should get inventory', async () => {
            await voucherService.getInventory();
            expect(mockVoucherRepository.getInventoryAggregation).toHaveBeenCalled();
        });

        it('should update voucher', async () => {
            await voucherService.updateVoucher('v1', { status: 'sold' });
            expect(mockVoucherRepository.update).toHaveBeenCalledWith('v1', { status: 'sold' });
        });

        it('should delete voucher', async () => {
            await voucherService.deleteVoucher('v1');
            expect(mockVoucherRepository.delete).toHaveBeenCalledWith('v1');
        });

        it('should delete all vouchers', async () => {
            await voucherService.deleteAllVouchers();
            expect(mockVoucherRepository.deleteAll).toHaveBeenCalled();
        });
    });

    describe('assignVouchersManually', () => {
        const userId = 'u1';
        const items = [{ station: 'OKKO', fuelType: 'A-95', liters: 20, quantity: 2 }];

        it('should assign vouchers successfully', async () => {
            mockVoucherRepository.assignToPurchase.mockResolvedValue([{ id: 'v1' }, { id: 'v2' }]);

            const results = await voucherService.assignVouchersManually(userId, items);

            expect(mockVoucherRepository.assignToPurchase).toHaveBeenCalledWith(
                0, userId, 'OKKO', 'A-95', 20, 2
            );
            expect(results).toHaveLength(1);
            expect(results[0].success).toBe(true);
            expect(results[0].assigned).toBe(2);
        });

        it('should handle assignment errors gracefully', async () => {
            mockVoucherRepository.assignToPurchase.mockRejectedValue(new Error('Out of stock'));

            const results = await voucherService.assignVouchersManually(userId, items);

            expect(results[0].success).toBe(false);
            expect(results[0].error).toBe('Out of stock');
        });
    });
});
