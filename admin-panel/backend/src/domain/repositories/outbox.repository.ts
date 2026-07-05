/**
 * Outbox Repository Interface
 * 
 * Defines persistence operations for Outbox events (transactional outbox pattern).
 */

import { IBaseRepository } from './base.repository';

/**
 * Outbox event types
 */
export type OutboxEventType =
    | 'ORDER_CREATED'
    | 'VOUCHERS_IMPORTED'
    | 'FULFILLMENT_COMPLETED'
    | 'PAYMENT_FAILED';

/**
 * Outbox entity (domain representation)
 */
export interface OutboxEvent {
    id: number;
    eventType: OutboxEventType;
    payload: Record<string, unknown>;
    processed: boolean;
    attempts: number;
    lastError: string | null;
    scheduledFor: Date;
    createdAt: Date;
    processedAt: Date | null;
}

/**
 * Outbox creation data
 */
export interface CreateOutboxEventData {
    eventType: OutboxEventType;
    payload: Record<string, unknown>;
    scheduledFor?: Date;
}

/**
 * Outbox Repository Interface
 */
export interface IOutboxRepository extends Omit<IBaseRepository<OutboxEvent, number>, 'create' | 'update' | 'delete'> {
    /**
     * Create a new outbox event
     */
    create(data: CreateOutboxEventData): Promise<OutboxEvent>;

    /**
     * Get unprocessed events ready for processing
     */
    findUnprocessed(limit?: number): Promise<OutboxEvent[]>;

    /**
     * Mark event as processed
     */
    markProcessed(id: number): Promise<void>;

    /**
     * Mark event as failed (increments attempts, records error)
     */
    markFailed(id: number, error: string): Promise<void>;

    /**
     * Reschedule event for retry
     */
    reschedule(id: number, scheduledFor: Date): Promise<void>;

    /**
     * Get failed events for retry
     */
    findFailedForRetry(maxAttempts: number): Promise<OutboxEvent[]>;

    /**
     * Delete old processed events (cleanup)
     */
    deleteProcessedBefore(date: Date): Promise<number>;
}
