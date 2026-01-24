
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { PurchaseService } from '../../src/application/services/purchase.service';
import { AppError } from '../../src/shared/errors/app-error';

describe('Purchase Service', () => {
    let purchaseService: PurchaseService;
    const mockPurchaseRepository = {
        createPurchase: vi.fn(),
        getPurchase: vi.fn(),
        updatePurchaseStatus: vi.fn(),
        getPurchasesByUserId: vi.fn(),
        getPurchaseWithQrCode: vi.fn(),
        getAllPurchases: vi.fn(),
    };
    const mockOrderRepository = {
        createWithEvent: vi.fn(),
    };
    const mockVoucherRepository = {
        assignToPurchase: vi.fn(),
    };

    beforeEach(() => {
        vi.clearAllMocks();
        purchaseService = new PurchaseService(
            mockPurchaseRepository as any,
            mockOrderRepository as any,
            mockVoucherRepository as any
        );
    });

    describe('createCheckout', () => {
        it('should create purchase with pending status', async () => {
            const request = {
                packageId: 'p1',
                stationId: 's1',
                stationName: 'OKKO',
                fuelType: 'A-95',
                fuelName: 'A-95 Euro',
                liters: 20,
                price: 1000,
            };
            mockPurchaseRepository.createPurchase.mockResolvedValue({ id: 101, status: 'pending' });

            const id = await purchaseService.createCheckout('u1', request);

            expect(id).toBe(101);
            expect(mockPurchaseRepository.createPurchase).toHaveBeenCalledWith(expect.objectContaining({
                sessionId: 'u1',
                status: 'pending'
            }));
        });
    });

    describe('simulatePayment', () => {
        const purchase = {
            id: 101,
            sessionId: 'u1',
            stationName: 'OKKO',
            fuelName: 'A-95',
            liters: 20,
            price: 1000,
            status: 'pending'
        };

        it('should handle success scenario', async () => {
            mockPurchaseRepository.getPurchase.mockResolvedValue(purchase);

            const result = await purchaseService.simulatePayment(101, 'success');

            expect(mockPurchaseRepository.updatePurchaseStatus).toHaveBeenCalledWith(101, 'paid');
            expect(mockOrderRepository.createWithEvent).toHaveBeenCalledWith(expect.objectContaining({
                status: 'PENDING_FULFILLMENT'
            }));
            expect(result.status).toBe('success');
        });

        it('should handle failure scenario', async () => {
            mockPurchaseRepository.getPurchase.mockResolvedValue(purchase);

            const result = await purchaseService.simulatePayment(101, 'failure');

            expect(mockPurchaseRepository.updatePurchaseStatus).toHaveBeenCalledWith(101, 'failed');
            expect(mockOrderRepository.createWithEvent).not.toHaveBeenCalled();
            expect(result.status).toBe('failed');
        });

        it('should throw if purchase not found', async () => {
            mockPurchaseRepository.getPurchase.mockResolvedValue(null);
            await expect(purchaseService.simulatePayment(999, 'success')).rejects.toThrow(AppError);
        });
    });

    describe('completePurchase (Legacy Flow)', () => {
        const purchase = {
            id: 101,
            sessionId: 'u1',
            stationName: 'OKKO',
            fuelName: 'A-95',
            liters: 20,
            price: 1000,
        };

        it('should assign voucher directly if available', async () => {
            mockPurchaseRepository.getPurchase.mockResolvedValue(purchase);
            mockVoucherRepository.assignToPurchase.mockResolvedValue([{ id: 'v1' }]);

            await purchaseService.completePurchase(101);

            expect(mockPurchaseRepository.updatePurchaseStatus).toHaveBeenCalledWith(101, 'delivered', undefined, 'v1');
        });

        it('should fallback to pending order if assignment fails', async () => {
            mockPurchaseRepository.getPurchase.mockResolvedValue(purchase);
            mockVoucherRepository.assignToPurchase.mockRejectedValue(new Error('Out of stock'));

            await purchaseService.completePurchase(101);

            expect(mockOrderRepository.createWithEvent).toHaveBeenCalled();
            expect(mockPurchaseRepository.updatePurchaseStatus).toHaveBeenCalledWith(101, 'pending');
        });
    });
});
