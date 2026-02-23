import { db } from "../shared/database/db";
import { eq, and, asc, isNull, inArray } from "drizzle-orm";
import {
    fulfillments,
    vouchers,
} from "../shared/database/schema";
import { ordersRepository } from "../features/orders/orders.repository";
import { getFuelAliases, getProviderAliases } from "../features/vouchers/vouchers.repository";
import {
    isRedisAvailable,
    initializeStreams,
    readFromStream,
    acknowledgeMessage,
    STREAMS,
    CONSUMER_GROUPS,
} from "../shared/infrastructure/redis";

import { logger } from '../infrastructure/logging/logger';

const log = logger.child({ component: 'FulfillmentConsumer' });

// Process ONE event at a time — this is what guarantees strict FIFO ordering.
// If a user bought 1 microsecond earlier, their Redis stream message is earlier,
// and with BATCH_SIZE=1 it is fully processed before the next message is read.
const POLL_INTERVAL_MS = 5000;
const REDIS_BLOCK_MS = 5000;
const BATCH_SIZE = 1; // MUST stay 1 for strict FIFO

interface OrderCreatedPayload {
    orderId: string;
    userId: string;
    provider: string;
    fuelType: string;
    liters: number;
    quantity: number;
}

interface VouchersImportedPayload {
    provider: string;
    fuelType: string;
    liters: number;
    count: number;
}

/**
 * Fulfillment Consumer Service
 * 
 * Supports two modes:
 * 1. Redis Streams (preferred) - Real-time event processing with consumer groups
 * 2. Database Outbox (fallback) - Polling-based when Redis is unavailable
 */
export class FulfillmentConsumer {
    private isRunning = false;
    private pollTimer: NodeJS.Timeout | null = null;
    private useRedis = false;
    private consumerName: string;

    constructor() {
        // Unique consumer name for this instance
        this.consumerName = `consumer-${process.pid}-${Date.now()}`;
    }

    /**
     * Start the consumer
     */
    async start(): Promise<void> {
        if (this.isRunning) {
            console.log("[FulfillmentConsumer] Already running");
            return;
        }

        this.isRunning = true;

        // Check if Redis is available
        this.useRedis = await isRedisAvailable();

        if (this.useRedis) {
            console.log("[FulfillmentConsumer] Starting with Redis Streams");
            await initializeStreams();
            this.consumeRedisStream();
        }

        // Always start outbox polling as a secondary/fallback mechanism
        console.log("[FulfillmentConsumer] Starting database outbox polling");
        this.pollOutbox();
    }

    /**
     * Stop the consumer
     */
    stop(): void {
        this.isRunning = false;
        if (this.pollTimer) {
            clearTimeout(this.pollTimer);
            this.pollTimer = null;
        }
        log.info('FulfillmentConsumer stopped');
    }

    /**
     * Consume events from Redis Streams
     */
    private async consumeRedisStream(): Promise<void> {
        while (this.isRunning) {
            try {
                // Read exactly 1 message at a time — guarantees strict FIFO processing.
                // The next message is not read until this one is fully committed.
                const messages = await readFromStream(
                    STREAMS.ORDER_EVENTS,
                    CONSUMER_GROUPS.FULFILLMENT_CONSUMER,
                    this.consumerName,
                    BATCH_SIZE,       // always 1
                    REDIS_BLOCK_MS
                );

                for (const message of messages) {
                    try {
                        const success = await this.processEvent({
                            id: message.id as any,
                            eventType: message.eventType,
                            payload: message.payload,
                        });

                        if (success) {
                            await acknowledgeMessage(
                                STREAMS.ORDER_EVENTS,
                                CONSUMER_GROUPS.FULFILLMENT_CONSUMER,
                                message.id
                            );
                            log.debug({ messageId: message.id }, 'Redis message acknowledged');
                        } else {
                            // Vouchers not yet available — leave in PEL for retry
                            log.info({ messageId: message.id }, 'Message not fully processed — staying in PEL');
                        }
                    } catch (error: any) {
                        log.error({ err: error, messageId: message.id }, 'Error processing Redis message');
                        // Not acknowledged — will be retried on next consumer start
                    }
                }
            } catch (error: any) {
                log.error({ err: error }, 'Redis stream error — switching to outbox fallback');
                if (this.isRunning) {
                    this.useRedis = false;
                    this.pollOutbox();
                    return;
                }
            }
        }
    }

    /**
     * Poll the database outbox (fallback mode)
     */
    private async pollOutbox(): Promise<void> {
        if (!this.isRunning) return;

        try {
            // Fetch ONE event at a time from the outbox — same FIFO guarantee as Redis mode.
            // outbox.created_at order is preserved via getUnprocessedEvents ORDER BY created_at ASC.
            const events = await ordersRepository.getUnprocessedEvents(1);

            for (const event of events) {
                try {
                    await this.processEvent(event);
                    await ordersRepository.markEventProcessed(event.id);
                } catch (error: any) {
                    log.error({ err: error, eventId: event.id }, 'Error processing outbox event');
                }
            }
        } catch (error: any) {
            log.error({ err: error }, 'Outbox poll error');
        }

        this.pollTimer = setTimeout(() => this.pollOutbox(), POLL_INTERVAL_MS);
    }

    /**
     * Process a single event (shared between Redis and outbox modes)
     * Returns true if processing was "complete" (allows ACKing in Redis)
     */
    private async processEvent(event: { id: number | string; eventType: string; payload: unknown }): Promise<boolean> {
        log.info({ eventId: event.id, eventType: event.eventType }, 'Processing event');

        switch (event.eventType) {
            case "ORDER_CREATED":
                return await this.handleOrderCreated(event.payload as OrderCreatedPayload);
            case "VOUCHERS_IMPORTED":
                return await this.handleVouchersImported(event.payload as VouchersImportedPayload);
            default:
                log.warn({ eventType: event.eventType }, 'Unknown event type — ACKing to clear');
                return true;
        }
    }

    /**
     * Handle ORDER_CREATED event - attempt to assign vouchers
     * Returns true if order is FULLY fulfilled
     */
    private async handleOrderCreated(payload: OrderCreatedPayload): Promise<boolean> {
        log.info({ orderId: payload.orderId }, 'Handling ORDER_CREATED');

        const order = await ordersRepository.getOrderById(payload.orderId);
        if (!order) {
            log.error({ orderId: payload.orderId }, 'Order not found — ACKing to prevent infinite retry');
            return true;
        }

        if (order.status !== "PENDING_FULFILLMENT") {
            log.info({ orderId: order.id, status: order.status }, 'Order already handled');
            return true;
        }

        let fulfilledCount = 0;
        for (let i = 0; i < order.quantity; i++) {
            const assigned = await this.findAndAssignVoucher(
                order.id,
                order.userId,
                order.provider,
                order.fuelType,
                order.liters
            );
            if (assigned) {
                fulfilledCount++;
            } else {
                break;
            }
        }

        if (fulfilledCount >= order.quantity) {
            await ordersRepository.updateOrderStatus(order.id, "FULFILLED", new Date());
            log.info({ orderId: order.id, count: fulfilledCount }, 'Order fully fulfilled');
            return true;
        } else {
            log.info({ orderId: order.id, fulfilled: fulfilledCount, total: order.quantity }, 'Order not fully fulfilled — vouchers pending');
            return false;
        }
    }

    /**
     * Handle VOUCHERS_IMPORTED event - backfill pending orders
     * Returns true when the batch scan is complete
     */
    private async handleVouchersImported(payload: VouchersImportedPayload): Promise<boolean> {
        console.log(
            `[FulfillmentConsumer] Vouchers imported: ${payload.count}x ${payload.provider} ${payload.fuelType} ${payload.liters}L`
        );

        // Get pending orders for this product type
        const pendingOrders = await ordersRepository.getPendingOrdersByProductType(
            payload.provider,
            payload.fuelType,
            payload.liters
        );

        console.log(`[FulfillmentConsumer] Found ${pendingOrders.length} pending orders to backfill`);

        // Directly fulfill each pending order
        for (const order of pendingOrders) {
            console.log(`[FulfillmentConsumer] Backfilling order ${order.id}`);

            // Try to assign vouchers for each quantity
            let fulfilledCount = 0;
            for (let i = 0; i < order.quantity; i++) {
                const assigned = await this.findAndAssignVoucher(
                    order.id,
                    order.userId,
                    order.provider,
                    order.fuelType,
                    order.liters
                );
                if (assigned) {
                    fulfilledCount++;
                } else {
                    break; // No more vouchers available
                }
            }

            if (fulfilledCount >= order.quantity) {
                // Fully fulfilled
                await ordersRepository.updateOrderStatus(order.id, "FULFILLED", new Date());
                console.log(`[FulfillmentConsumer] Order ${order.id} FULFILLED with ${fulfilledCount} vouchers`);
            }
        }

        return true; // The import event itself is fully processed (scan finished)
    }

    /**
     * Find an available voucher and assign it to an order
     */
    private async findAndAssignVoucher(
        orderId: string,
        userId: string,
        provider: string,
        fuelType: string,
        liters: number
    ): Promise<boolean> {
        return await db.transaction(async (tx: any) => {
            // FIFO ordering is guaranteed at the queue level (BATCH_SIZE=1 means only
            // one order is ever processed at a time). Here, SKIP LOCKED restores
            // performance: if somehow two transactions ran concurrently (e.g. Redis +
            // outbox both active), they'd each grab a different voucher quickly rather
            // than blocking each other. Ordering is the queue's responsibility, not the DB's.
            const [voucher] = await tx
                .select()
                .from(vouchers)
                .where(
                    and(
                        inArray(vouchers.provider, getProviderAliases(provider)),
                        inArray(vouchers.fuelType, getFuelAliases(fuelType)),
                        eq(vouchers.amount, liters),
                        eq(vouchers.status, "available"),
                        isNull(vouchers.assignedToUserId)
                    )
                )
                .orderBy(asc(vouchers.createdAt))  // oldest voucher first
                .limit(1)
                .for("update", { skipLocked: true });

            if (!voucher) {
                return false;
            }

            await tx
                .update(vouchers)
                .set({
                    assignedToUserId: userId,
                    status: "sold",
                    updatedAt: new Date(),
                })
                .where(eq(vouchers.id, voucher.id));

            await tx.insert(fulfillments).values({
                orderId,
                voucherId: voucher.id,
            });

            log.debug({ orderId, voucherId: voucher.id }, 'Voucher assigned');
            return true;
        });
    }
}

// Singleton instance
export const fulfillmentConsumer = new FulfillmentConsumer();
