
import { describe, it, expect, vi, afterAll } from 'vitest';
import { DrizzleVoucherRepository } from '../../src/infrastructure/persistence/drizzle/repositories/drizzle-voucher.repository';
import { runInTransaction, closePool } from './db-helper';
import { vouchers } from '../../src/shared/database/schema';

describe('Drizzle Voucher Repository', () => {
    afterAll(async () => {
        await closePool();
    });

    it('should create and find voucher', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleVoucherRepository(tx);
            const data = {
                provider: 'UPG',
                fuelType: 'A-95',
                amount: 10,
                unit: 'L',
                status: 'available' as const,
                source: 'test',
                importJobId: 'job1'
            };

            const created = await repository.create(data);
            expect(created.id).toBeDefined();

            const found = await repository.findById(created.id);
            expect(found).toBeDefined();
            expect(found?.provider).toBe('UPG');
        });
    });

    it('should find available vouchers matching criteria', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleVoucherRepository(tx);

            // Insert available voucher
            await repository.create({
                provider: 'OKKO', fuelType: 'A-95', amount: 20, status: 'available', source: 't'
            });
            // Insert sold voucher
            await repository.create({
                provider: 'OKKO', fuelType: 'A-95', amount: 20, status: 'sold', source: 't'
            });

            // Search
            const found = await repository.findAvailable('OKKO', 'A-95', 20);
            expect(found).toBeDefined();
            expect(found?.status).toBe('available');
        });
    });

    it('should assign voucher to purchase transactionally', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleVoucherRepository(tx);

            // Create 3 available vouchers
            await repository.create({ provider: 'OKKO', fuelType: 'A-95', amount: 20, status: 'available', source: 't' });
            await repository.create({ provider: 'OKKO', fuelType: 'A-95', amount: 20, status: 'available', source: 't' });
            await repository.create({ provider: 'OKKO', fuelType: 'A-95', amount: 20, status: 'available', source: 't' });

            // Assign 2
            const assigned = await repository.assignToPurchase(101, 'u1', 'OKKO', 'A-95', 20, 2);
            expect(assigned).toHaveLength(2);
            expect(assigned[0].status).toBe('sold');
            expect(assigned[0].assignedToUserId).toBe('u1');
        });
    });

    it('should aggregate inventory', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleVoucherRepository(tx);

            await repository.create({ provider: 'A', fuelType: 'F', amount: 10, status: 'available', source: 't' });
            await repository.create({ provider: 'A', fuelType: 'F', amount: 10, status: 'available', source: 't' });
            await repository.create({ provider: 'B', fuelType: 'F', amount: 10, status: 'available', source: 't' });

            const inventory = await repository.getInventoryAggregation();

            const itemA = inventory.find(i => i.provider === 'A');
            expect(itemA?.availableCount).toBe(2);
            const itemB = inventory.find(i => i.provider === 'B');
            expect(itemB?.availableCount).toBe(1);
        });
    });
});
