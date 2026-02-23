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

const POLL_INTERVAL_MS = 5000; // Poll every 5 seconds (fallback)
const REDIS_BLOCK_MS = 5000; // Block for 5 seconds when reading from Redis
const BATCH_SIZE = 10;

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
        console.log("[FulfillmentConsumer] Stopped");
    }

    /**
     * Consume events from Redis Streams
     */
    private async consumeRedisStream(): Promise<void> {
        while (this.isRunning) {
            try {
                const messages = await readFromStream(
                    STREAMS.ORDER_EVENTS,
                    CONSUMER_GROUPS.FULFILLMENT_CONSUMER,
                    this.consumerName,
                    BATCH_SIZE,
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
                            // Acknowledge successful processing
                            await acknowledgeMessage(
                                STREAMS.ORDER_EVENTS,
                                CONSUMER_GROUPS.FULFILLMENT_CONSUMER,
                                message.id
                            );
                        } else {
                            console.log(
                                `[FulfillmentConsumer] Redis message ${message.id} not fully processed (vouchers likely missing). keeping in PEL.`
                            );
                            // DO NOT acknowledge - will remain in PEL
                        }
                    } catch (error: any) {
                        console.error(
                            `[FulfillmentConsumer] Error processing Redis message ${message.id}:`,
                            error.message
                        );
                        // Don't acknowledge - will be retried
                    }
                }
            } catch (error: any) {
                console.error("[FulfillmentConsumer] Redis stream error:", error.message);

                // If Redis fails, switch to fallback mode
                if (this.isRunning) {
                    console.log("[FulfillmentConsumer] Switching to database outbox fallback");
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
            const events = await ordersRepository.getUnprocessedEvents(BATCH_SIZE);

            for (const event of events) {
                try {
                    await this.processEvent(event);
                    await ordersRepository.markEventProcessed(event.id);
                } catch (error: any) {
                    console.error(
                        `[FulfillmentConsumer] Error processing outbox event ${event.id}:`,
                        error.message
                    );
                    // Don't mark as processed - will retry on next poll
                }
            }
        } catch (error: any) {
            console.error("[FulfillmentConsumer] Outbox poll error:", error.message);
        }

        // Schedule next poll
        this.pollTimer = setTimeout(() => this.pollOutbox(), POLL_INTERVAL_MS);
    }

    /**
     * Process a single event (shared between Redis and outbox modes)
     * Returns true if processing was "complete" (allows ACKing in Redis)
     */
    private async processEvent(event: { id: number | string; eventType: string; payload: unknown }): Promise<boolean> {
        console.log(`[FulfillmentConsumer] Processing event ${event.id}: ${event.eventType}`);

        switch (event.eventType) {
            case "ORDER_CREATED":
                return await this.handleOrderCreated(event.payload as OrderCreatedPayload);
            case "VOUCHERS_IMPORTED":
                return await this.handleVouchersImported(event.payload as VouchersImportedPayload);
            default:
                console.log(`[FulfillmentConsumer] Unknown event type: ${event.eventType}`);
                return true; // ACK unknown events to clear them
        }
    }

    /**
     * Handle ORDER_CREATED event - attempt to assign vouchers
     * Returns true if order is FULLY fulfilled
     */
    private async handleOrderCreated(payload: OrderCreatedPayload): Promise<boolean> {
        console.log(`[FulfillmentConsumer] Processing order ${payload.orderId}`);

        const order = await ordersRepository.getOrderById(payload.orderId);
        if (!order) {
            console.error(`[FulfillmentConsumer] Order not found: ${payload.orderId}`);
            return true; // Return true to ACK so we don't retry non-existent orders
        }

        if (order.status !== "PENDING_FULFILLMENT") {
            console.log(`[FulfillmentConsumer] Order ${payload.orderId} is not pending (${order.status})`);
            return true; // Already handled
        }

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
            console.log(`[FulfillmentConsumer] Order ${order.id} fully fulfilled with ${fulfilledCount} vouchers`);
            return true;
        } else {
            console.log(`[FulfillmentConsumer] Order ${order.id} NOT fully fulfilled (${fulfilledCount}/${order.quantity})`);
            return false; // NOT fully fulfilled
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
            // Find oldest unassigned voucher matching criteria
            const [voucher] = await tx
                .select()
                .from(vouchers)
                .where(
                    and(
                        // Broad provider match (Latin/Cyrillic aliases)
                        inArray(vouchers.provider, getProviderAliases(provider)),
                        // Broad fuel type match (Latin/Cyrillic aliases)
                        inArray(vouchers.fuelType, getFuelAliases(fuelType)),
                        eq(vouchers.amount, liters),
                        eq(vouchers.status, "available"), // Use 'available' as target status
                        isNull(vouchers.assignedToUserId)
                    )
                )
                .orderBy(asc(vouchers.createdAt))
                .limit(1)
                .for("update", { skipLocked: true }); // Row-level lock, skip if locked

            if (!voucher) {
                return false;
            }

            // Assign voucher to user
            await tx
                .update(vouchers)
                .set({
                    assignedToUserId: userId,
                    status: "sold",
                    updatedAt: new Date(),
                })
                .where(eq(vouchers.id, voucher.id));

            // Create fulfillment record
            await tx.insert(fulfillments).values({
                orderId,
                voucherId: voucher.id,
            });

            console.log(`[FulfillmentConsumer] Assigned voucher ${voucher.id} to order ${orderId}`);
            return true;
        });
    }
}

// Singleton instance
export const fulfillmentConsumer = new FulfillmentConsumer();
