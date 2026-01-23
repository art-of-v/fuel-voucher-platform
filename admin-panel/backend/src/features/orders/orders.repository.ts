import { db } from "../../shared/database/db";
import { eq, and, asc } from "drizzle-orm";
import {
    orders,
    fulfillments,
    outbox,
    type Order,
    type InsertOrder,
    type Fulfillment,
    type InsertFulfillment,
    type OutboxEvent,
} from "../../shared/database/schema";
import { isRedisAvailable, publishToStream, STREAMS } from "../../shared/infrastructure/redis";
import { getFuelAliases } from "../vouchers/vouchers.repository"; // Import alias helper
import { inArray } from "drizzle-orm";

export const ordersRepository = {
    /**
     * Create an order with an outbox event in a single transaction.
     * Also publishes to Redis Streams if available for real-time processing.
     */
    async createOrderWithEvent(order: InsertOrder): Promise<Order> {
        const created = await db.transaction(async (tx) => {
            // 1. Create the order
            const [newOrder] = await tx.insert(orders).values(order).returning();

            // 2. Create outbox event for async processing (always, for reliability)
            await tx.insert(outbox).values({
                eventType: "ORDER_CREATED",
                payload: {
                    orderId: newOrder.id,
                    userId: newOrder.userId,
                    provider: newOrder.provider,
                    fuelType: newOrder.fuelType,
                    liters: newOrder.liters,
                    quantity: newOrder.quantity,
                },
                processed: 0,
            });

            return newOrder;
        });

        // 3. Also publish to Redis Streams for real-time processing (best effort)
        try {
            if (await isRedisAvailable()) {
                await publishToStream(STREAMS.ORDER_EVENTS, "ORDER_CREATED", {
                    orderId: created.id,
                    userId: created.userId,
                    provider: created.provider,
                    fuelType: created.fuelType,
                    liters: created.liters,
                    quantity: created.quantity,
                });
            }
        } catch (err: any) {
            console.warn(`[OrdersRepository] Failed to publish to Redis: ${err.message}`);
            // Outbox will handle it as fallback
        }

        return created;
    },

    /**
     * Get order by ID
     */
    async getOrderById(id: string): Promise<Order | undefined> {
        const [order] = await db.select().from(orders).where(eq(orders.id, id));
        return order;
    },

    /**
     * Get order by idempotency key (for duplicate prevention)
     */
    async getOrderByIdempotencyKey(key: string): Promise<Order | undefined> {
        const [order] = await db
            .select()
            .from(orders)
            .where(eq(orders.idempotencyKey, key));
        return order;
    },

    /**
     * Get all orders for a user
     */
    async getOrdersByUserId(userId: string): Promise<Order[]> {
        return await db
            .select()
            .from(orders)
            .where(eq(orders.userId, userId))
            .orderBy(asc(orders.createdAt));
    },

    /**
     * Get pending orders for a specific product type (for FIFO fulfillment)
     */
    async getPendingOrdersByProductType(
        provider: string,
        fuelType: string,
        liters: number
    ): Promise<Order[]> {
        return await db
            .select()
            .from(orders)
            .where(
                and(
                    eq(orders.status, "PENDING_FULFILLMENT"),
                    eq(orders.provider, provider), // We can keep strict provider for now, or match lowercase if needed
                    inArray(orders.fuelType, getFuelAliases(fuelType)), // Use aliases heavily here
                    eq(orders.liters, liters)
                )
            )
            .orderBy(asc(orders.createdAt)); // FIFO ordering
    },

    /**
     * Get all pending orders (for backfill processing)
     */
    async getAllPendingOrders(): Promise<Order[]> {
        return await db
            .select()
            .from(orders)
            .where(eq(orders.status, "PENDING_FULFILLMENT"))
            .orderBy(asc(orders.createdAt));
    },

    /**
     * Update order status
     */
    async updateOrderStatus(
        orderId: string,
        status: string,
        fulfilledAt?: Date
    ): Promise<void> {
        const updates: Partial<Order> = { status };
        if (fulfilledAt) {
            updates.fulfilledAt = fulfilledAt;
        }
        await db.update(orders).set(updates).where(eq(orders.id, orderId));
    },

    /**
     * Create a fulfillment record (links order to voucher)
     */
    async createFulfillment(fulfillment: InsertFulfillment): Promise<Fulfillment> {
        const [created] = await db
            .insert(fulfillments)
            .values(fulfillment)
            .returning();
        return created;
    },

    /**
     * Get fulfillments for an order
     */
    async getFulfillmentsByOrderId(orderId: string): Promise<Fulfillment[]> {
        return await db
            .select()
            .from(fulfillments)
            .where(eq(fulfillments.orderId, orderId));
    },

    /**
     * Get unprocessed outbox events
     */
    async getUnprocessedEvents(limit: number = 10): Promise<OutboxEvent[]> {
        return await db
            .select()
            .from(outbox)
            .where(eq(outbox.processed, 0))
            .orderBy(asc(outbox.createdAt))
            .limit(limit);
    },

    /**
     * Mark outbox event as processed
     */
    async markEventProcessed(eventId: number): Promise<void> {
        await db
            .update(outbox)
            .set({ processed: 1, processedAt: new Date() })
            .where(eq(outbox.id, eventId));
    },

    /**
     * Publish an event to the outbox (for voucher import notifications)
     */
    async publishEvent(
        eventType: string,
        payload: Record<string, unknown>
    ): Promise<void> {
        await db.insert(outbox).values({
            eventType,
            payload,
            processed: 0,
        });
    },
};
