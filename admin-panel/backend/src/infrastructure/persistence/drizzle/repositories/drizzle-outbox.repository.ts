/**
 * Drizzle Outbox Repository
 * 
 * Concrete implementation of IOutboxRepository using Drizzle ORM.
 */

import { db } from '../../../../shared/database/db';
import { eq, and, asc, lt } from 'drizzle-orm';
import { outbox, type OutboxEvent as DbOutboxEvent } from '../../../../shared/database/schema';
import type {
    IOutboxRepository,
    OutboxEvent,
    OutboxEventType,
    CreateOutboxEventData
} from '../../../../domain/repositories/outbox.repository';

function mapToDomain(dbEvent: DbOutboxEvent): OutboxEvent {
    return {
        id: dbEvent.id,
        eventType: dbEvent.eventType as OutboxEventType,
        payload: dbEvent.payload as Record<string, unknown>,
        processed: dbEvent.processed === 1,
        attempts: 0,
        lastError: null,
        scheduledFor: dbEvent.createdAt,
        createdAt: dbEvent.createdAt,
        processedAt: dbEvent.processedAt,
    };
}

export class DrizzleOutboxRepository implements IOutboxRepository {
    async findById(id: number): Promise<OutboxEvent | null> {
        const [event] = await db.select().from(outbox).where(eq(outbox.id, id));
        return event ? mapToDomain(event) : null;
    }

    async findAll(): Promise<OutboxEvent[]> {
        const events = await db.select().from(outbox);
        return events.map(mapToDomain);
    }

    async create(data: CreateOutboxEventData): Promise<OutboxEvent> {
        const [event] = await db.insert(outbox).values({
            eventType: data.eventType,
            payload: data.payload,
            processed: 0,
        }).returning();
        return mapToDomain(event);
    }

    async findUnprocessed(limit: number = 10): Promise<OutboxEvent[]> {
        const events = await db
            .select()
            .from(outbox)
            .where(eq(outbox.processed, 0))
            .orderBy(asc(outbox.createdAt))
            .limit(limit);
        return events.map(mapToDomain);
    }

    async markProcessed(id: number): Promise<void> {
        await db
            .update(outbox)
            .set({ processed: 1, processedAt: new Date() })
            .where(eq(outbox.id, id));
    }

    async markFailed(_id: number, error: string): Promise<void> {
        console.warn(`[OutboxRepository] Event ${_id} failed: ${error}`);
    }

    async reschedule(_id: number, _scheduledFor: Date): Promise<void> {
        console.warn(`[OutboxRepository] Reschedule not fully supported in current schema`);
    }

    async findFailedForRetry(_maxAttempts: number): Promise<OutboxEvent[]> {
        return this.findUnprocessed(10);
    }

    async deleteProcessedBefore(date: Date): Promise<number> {
        const result = await db
            .delete(outbox)
            .where(
                and(
                    eq(outbox.processed, 1),
                    lt(outbox.processedAt, date)
                )
            )
            .returning({ id: outbox.id });
        return result.length;
    }

    async save(entity: OutboxEvent): Promise<OutboxEvent> {
        await db.update(outbox).set({
            processed: entity.processed ? 1 : 0,
            processedAt: entity.processedAt,
        }).where(eq(outbox.id, entity.id));
        return entity;
    }

    async count(): Promise<number> {
        const result = await db.select().from(outbox);
        return result.length;
    }

    async exists(id: number): Promise<boolean> {
        const [event] = await db.select({ id: outbox.id }).from(outbox).where(eq(outbox.id, id));
        return !!event;
    }

    async delete(id: number): Promise<void> {
        await db.delete(outbox).where(eq(outbox.id, id));
    }
}

export const drizzleOutboxRepository = new DrizzleOutboxRepository();
