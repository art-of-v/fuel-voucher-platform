/**
 * Transaction Manager
 * 
 * Provides centralized transaction management for the application.
 * Ensures all write operations are properly scoped within transactions.
 */

import { db } from '../../shared/database/db';
import { PgTransaction } from 'drizzle-orm/pg-core';
import { ExtractTablesWithRelations } from 'drizzle-orm';
import { PgQueryResultHKT } from 'drizzle-orm/pg-core';

export type Transaction = PgTransaction<
    PgQueryResultHKT,
    Record<string, never>,
    ExtractTablesWithRelations<Record<string, never>>
>;

/**
 * Transaction Manager
 * 
 * Coordinates database transactions across the application.
 */
export class TransactionManager {
    /**
     * Execute work within a transaction
     * 
     * @param work - Function to execute within transaction context
     * @returns Result of the work function
     */
    async executeInTransaction<T>(
        work: (tx: Transaction) => Promise<T>
    ): Promise<T> {
        return db.transaction(async (tx) => {
            try {
                const result = await work(tx as Transaction);
                // Transaction auto-commits on successful completion
                return result;
            } catch (error) {
                // Transaction auto-rolls back on error
                throw error;
            }
        });
    }

    /**
     * Execute work with retry logic
     * 
     * @param work - Function to execute
     * @param maxRetries - Maximum number of retries
     * @returns Result of the work function
     */
    async executeWithRetry<T>(
        work: (tx: Transaction) => Promise<T>,
        maxRetries: number = 3
    ): Promise<T> {
        let lastError: Error | null = null;

        for (let attempt = 0; attempt <= maxRetries; attempt++) {
            try {
                return await this.executeInTransaction(work);
            } catch (error) {
                lastError = error as Error;

                // Check if error is retryable (e.g., deadlock, serialization failure)
                const isRetryable = this.isRetryableError(error);

                if (!isRetryable || attempt === maxRetries) {
                    throw error;
                }

                // Exponential backoff
                const delay = Math.min(100 * Math.pow(2, attempt), 1000);
                await this.sleep(delay);
            }
        }

        throw lastError;
    }

    /**
     * Check if error is retryable
     */
    private isRetryableError(error: any): boolean {
        const retryableCodes = [
            '40001', // serialization_failure
            '40P01', // deadlock_detected
        ];

        return retryableCodes.includes(error?.code);
    }

    /**
     * Sleep utility
     */
    private sleep(ms: number): Promise<void> {
        return new Promise(resolve => setTimeout(resolve, ms));
    }
}

// Singleton instance
export const transactionManager = new TransactionManager();
