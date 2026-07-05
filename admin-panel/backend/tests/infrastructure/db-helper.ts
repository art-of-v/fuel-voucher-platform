
import { drizzle } from 'drizzle-orm/node-postgres';
import pg from 'pg';
import * as schema from '../../src/shared/database/schema';

// Use the port mapped in docker-compose (5433 -> 5432)
const connectionString = process.env.DATABASE_URL || 'postgres://postgres:postgres@localhost:5433/fuel_db';

const pool = new pg.Pool({ connectionString });
export const testDb = drizzle(pool, { schema });

export const runInTransaction = async (callback: (tx: any) => Promise<void>) => {
    // Start a transaction
    try {
        await testDb.transaction(async (tx) => {
            try {
                await callback(tx);
            } finally {
                // Always rollback to keep DB clean
                await tx.rollback();
            }
        });
    } catch (e: any) {
        // Ignore rollback error, propagate others
        if (e.constructor.name !== 'Rollback' && e.message !== 'Rollback') {
            // If validation failed inside test, rethrow
            throw e;
        }
    }
};

export const closePool = async () => {
    await pool.end();
};
