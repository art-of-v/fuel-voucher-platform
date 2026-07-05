
import { describe, it, expect, vi, afterAll } from 'vitest';
import { DrizzleUserRepository } from '../../src/infrastructure/persistence/drizzle/repositories/drizzle-user.repository';
import { runInTransaction, closePool } from './db-helper';
import { users } from '../../src/shared/database/schema';

describe('Drizzle User Repository', () => {
    afterAll(async () => {
        await closePool();
    });

    it('should create and find user', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleUserRepository(tx);
            const data = {
                firstName: 'John',
                lastName: 'Doe',
                phone: '+1234567890',
                email: 'john@example.com'
            };

            const created = await repository.create(data);
            expect(created.id).toBeDefined();
            expect(created.phone).toBe(data.phone);

            const found = await repository.findById(created.id);
            expect(found).toBeDefined();
            expect(found?.email).toBe(data.email);
        });
    });

    it('should find by phone', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleUserRepository(tx);
            await repository.create({ phone: '+999' });

            const found = await repository.findByPhone('+999');
            expect(found).toBeDefined();
        });
    });

    it('should upsert user', async () => {
        await runInTransaction(async (tx) => {
            const repository = new DrizzleUserRepository(tx);
            const id = 'user-upsert-1';

            // First insert
            await repository.upsert({ id, firstName: 'A' } as any);
            let u = await repository.findById(id);
            expect(u?.firstName).toBe('A');

            // Update
            await repository.upsert({ id, firstName: 'B' } as any);
            u = await repository.findById(id);
            expect(u?.firstName).toBe('B');
            expect(u?.updatedAt.getTime()).toBeGreaterThan(u?.createdAt.getTime() || 0);
        });
    });
});
