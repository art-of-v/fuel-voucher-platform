
import { describe, it, expect, vi, afterAll } from 'vitest';
import { DrizzleOrderRepository } from '../../src/infrastructure/persistence/drizzle/repositories/drizzle-order.repository';
import { runInTransaction, closePool } from './db-helper';
import { orders } from '../../src/shared/database/schema';
import { eq } from 'drizzle-orm';

// Mock Redis to avoid side effects
vi.mock('../../src/infrastructure/events/redis', () => ({
    isRedisAvailable: vi.fn().mockResolvedValue(false),
    publishToStream: vi.fn(),
    STREAMS: { ORDER_EVENTS: 'ORDER_EVENTS' },
}));

describe('Drizzle Order Repository', () => {
    afterAll(async () => {
        await closePool();
    });

    it('should create and retrieve an order', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleOrderRepository(tx);

            const data = {
                userId: 'user-test-1',
                productType: 'OKKO A-95',
                provider: 'OKKO',
                fuelType: 'A-95',
                liters: 20,
                quantity: 1,
                price: 1000,
                status: 'PENDING_FULFILLMENT' as const,
                idempotencyKey: 'key-1'
            };

            const created = await repository.create(data);
            expect(created.id).toBeDefined();
            expect(created.userId).toBe(data.userId);

            const found = await repository.findById(created.id);
            expect(found).toBeDefined();
            expect(found?.id).toBe(created.id);
            expect(found?.status).toBe('PENDING_FULFILLMENT');
        });
    });

    it('should find pending orders by product type FIFO', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleOrderRepository(tx);

            // Create 3 orders with different timestamps
            // We can't easily force timestamps via repository.create as it uses defaultNow() or passed date?
            // create() doesn't accept createdAt.
            // We'll insert raw for setup.

            const common = {
                userId: 'u1',
                productType: 'P',
                provider: 'TEST',
                fuelType: 'A-95',
                liters: 10,
                quantity: 1,
                price: 100,
                status: 'PENDING_FULFILLMENT',
                updatedAt: new Date(),
            };

            const [o1] = await tx.insert(orders).values({ ...common, id: 'o1', createdAt: new Date('2023-01-01') }).returning();
            const [o2] = await tx.insert(orders).values({ ...common, id: 'o2', createdAt: new Date('2023-01-02') }).returning();
            const [o3] = await tx.insert(orders).values({ ...common, id: 'o3', createdAt: new Date('2023-01-03') }).returning();

            // Search
            const result = await repository.findPendingByProductType('TEST', 'A-95', 10);

            expect(result).toHaveLength(3);
            expect(result[0].id).toBe('o1'); // First created
            expect(result[1].id).toBe('o2');
            expect(result[2].id).toBe('o3');
        });
    });

    it('should update status', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleOrderRepository(tx);
            const created = await repository.create({
                userId: 'u1', productType: 'P', provider: 'P', fuelType: 'F', liters: 10, quantity: 1, price: 10
            });

            await repository.updateStatus(created.id, 'FULFILLED', new Date());

            const found = await repository.findById(created.id);
            expect(found?.status).toBe('FULFILLED');
            expect(found?.fulfilledAt).toBeDefined();
        });
    });
});
