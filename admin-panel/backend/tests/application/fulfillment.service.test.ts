
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { FulfillmentService } from '../../src/application/services/fulfillment.service';

// Mock DB before import
const { mockTx } = vi.hoisted(() => {
    return {
        mockTx: {
            select: vi.fn(),
            update: vi.fn(),
            insert: vi.fn(),
        }
    };
});

vi.mock('../../src/shared/database/db', () => ({
    db: {
        transaction: vi.fn((cb) => cb(mockTx)),
        select: mockTx.select,
        update: mockTx.update,
        insert: mockTx.insert,
    },
}));

describe('Fulfillment Service', () => {
    let fulfillmentService: FulfillmentService;
    const mockOrderRepository = {
        updateStatus: vi.fn(),
        findPendingByProductType: vi.fn(),
    };
    const mockOutboxRepository = {};

    beforeEach(() => {
        vi.clearAllMocks();

        // Reset chain mocks
        const selectChain = {
            from: vi.fn().mockReturnThis(),
            where: vi.fn().mockReturnThis(),
            orderBy: vi.fn().mockReturnThis(),
            limit: vi.fn().mockReturnThis(),
            for: vi.fn().mockResolvedValue([]), // Default no voucher
        };
        mockTx.select.mockReturnValue(selectChain);

        const updateChain = {
            set: vi.fn().mockReturnThis(),
            where: vi.fn().mockResolvedValue([]),
        };
        mockTx.update.mockReturnValue(updateChain);

        const insertChain = {
            values: vi.fn().mockResolvedValue([]),
        };
        mockTx.insert.mockReturnValue(insertChain);

        fulfillmentService = new FulfillmentService(
            mockOrderRepository as any,
            mockOutboxRepository as any
        );
    });

    describe('handleOrderCreated', () => {
        const payload = {
            orderId: 'o1',
            userId: 'u1',
            provider: 'OKKO',
            fuelType: 'A-95',
            liters: 20,
            quantity: 1,
        };

        it('should fulfill order if voucher available', async () => {
            // Mock finding voucher
            const voucher = { id: 'v1', amount: 20 };
            const selectChain = {
                from: vi.fn().mockReturnThis(),
                where: vi.fn().mockReturnThis(),
                orderBy: vi.fn().mockReturnThis(),
                limit: vi.fn().mockReturnThis(),
                for: vi.fn().mockResolvedValue([voucher]),
            };
            mockTx.select.mockReturnValue(selectChain);

            await fulfillmentService.handleOrderCreated(payload);

            expect(mockTx.update).toHaveBeenCalled(); // Voucher status update
            expect(mockTx.insert).toHaveBeenCalled(); // Fulfillment record
            expect(mockOrderRepository.updateStatus).toHaveBeenCalledWith('o1', 'FULFILLED', expect.any(Date));
        });

        it('should leave pending if no voucher', async () => {
            const selectChain = {
                from: vi.fn().mockReturnThis(),
                where: vi.fn().mockReturnThis(),
                orderBy: vi.fn().mockReturnThis(),
                limit: vi.fn().mockReturnThis(),
                for: vi.fn().mockResolvedValue([]), // Empty
            };
            mockTx.select.mockReturnValue(selectChain);

            await fulfillmentService.handleOrderCreated(payload);

            expect(mockTx.update).not.toHaveBeenCalled();
            expect(mockOrderRepository.updateStatus).not.toHaveBeenCalled();
        });

        it('should partially fulfill if only some vouchers available', async () => {
            const multiPayload = { ...payload, quantity: 2 };

            // First call finds voucher, second finds none
            const selectChain = {
                from: vi.fn().mockReturnThis(),
                where: vi.fn().mockReturnThis(),
                orderBy: vi.fn().mockReturnThis(),
                limit: vi.fn().mockReturnThis(),
                for: vi.fn()
                    .mockResolvedValueOnce([{ id: 'v1' }])
                    .mockResolvedValueOnce([]),
            };
            mockTx.select.mockReturnValue(selectChain);

            await fulfillmentService.handleOrderCreated(multiPayload);

            expect(mockOrderRepository.updateStatus).toHaveBeenCalledWith('o1', 'PARTIALLY_FULFILLED');
        });
    });

    describe('handleVouchersImported', () => {
        const payload = {
            provider: 'OKKO',
            fuelType: 'A-95',
            liters: 20,
            count: 5,
        };

        it('should backfill pending orders', async () => {
            const pendingOrder = { id: 'o1', userId: 'u1', provider: 'OKKO', fuelType: 'A-95', liters: 20, quantity: 1 };
            mockOrderRepository.findPendingByProductType.mockResolvedValue([pendingOrder]);

            // Mock getFulfilledCount (which uses db.select also)
            // wait, getFulfilledCount is private and uses db.select. I mocked db.select.
            // FulfillmentService calls getFulfilledCount -> db.select...
            // I need to ensure db.select works for both findAndAssignVoucher AND getFulfilledCount.

            // Logic: 
            // 1. findPendingByProductType -> returns order
            // 2. getFulfilledCount -> db.select(fulfillments)... -> returns [] (0 fulfilled)
            // 3. findAndAssignVoucher -> db.select(vouchers)... -> returns [voucher]

            const selectMock = vi.fn();
            mockTx.select.mockImplementation(selectMock);

            // getFulfilledCount chain
            const fulfillmentsChain = {
                from: vi.fn().mockReturnThis(),
                where: vi.fn().mockResolvedValue([]), // 0 existing fulfillments
            };

            // voucher chain
            const voucherChain = {
                from: vi.fn().mockReturnThis(),
                where: vi.fn().mockReturnThis(),
                orderBy: vi.fn().mockReturnThis(),
                limit: vi.fn().mockReturnThis(),
                for: vi.fn().mockResolvedValue([{ id: 'v1' }]),
            };

            // Using mockImplementation to switch based on table? 
            // Actually the service does: db.select().from(fulfillments) vs db.select().from(vouchers).
            // But 'fulfillments' and 'vouchers' are schema objects.
            // I'll assume sequential calls for simplicity or check arguments.

            // Call 1: getFulfilledCount -> returns []
            // Call 2: findAndAssignVoucher -> returns [voucher]

            // Since `select` returns a chain, I need to make the chain smart or return different chains.
            // Simplified: return a chain that can handle both or mockReturnValueOnce.

            // Because getFulfilledCount DOES NOT use .for('update'), but findAndAssignVoucher DOES.
            // I can use that to distinguish.

            // Chain for getFulfilledCount (no .for)
            const countQuery = {
                from: vi.fn().mockReturnThis(),
                where: vi.fn().mockResolvedValue([]),
            };

            // Chain for vouchers (has .for)
            const voucherQuery = {
                from: vi.fn().mockReturnThis(),
                where: vi.fn().mockReturnThis(),
                orderBy: vi.fn().mockReturnThis(),
                limit: vi.fn().mockReturnThis(),
                for: vi.fn().mockResolvedValue([{ id: 'v1' }]),
            };

            // This is getting messy because they share `select().from(...)`.
            // I'll rely on `mockReturnValueOnce` on `select`.

            mockTx.select
                .mockReturnValueOnce(countQuery) // getFulfilledCount
                .mockReturnValueOnce(voucherQuery); // findAndAssignVoucher

            await fulfillmentService.handleVouchersImported(payload);

            expect(mockOrderRepository.updateStatus).toHaveBeenCalledWith('o1', 'FULFILLED', expect.any(Date));
        });
    });
});
