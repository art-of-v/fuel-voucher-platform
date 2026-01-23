import { db } from "../shared/database/db";
import { eq, and, asc, isNull, inArray, sql } from "drizzle-orm";
import {
    fulfillments,
    vouchers,
} from "../shared/database/schema";
import { ordersRepository } from "../features/orders/orders.repository";
import { getFuelAliases } from "../features/vouchers/vouchers.repository";
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
        } else {
            console.log("[FulfillmentConsumer] Redis unavailable, falling back to database outbox polling");
            this.pollOutbox();
        }
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
                        await this.processEvent({
                            id: message.id as any,
                            eventType: message.eventType,
                            payload: message.payload,
                        });

                        // Acknowledge successful processing
                        await acknowledgeMessage(
                            STREAMS.ORDER_EVENTS,
                            CONSUMER_GROUPS.FULFILLMENT_CONSUMER,
                            message.id
                        );
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
     */
    private async processEvent(event: { id: number | string; eventType: string; payload: unknown }): Promise<void> {
        console.log(`[FulfillmentConsumer] Processing event ${event.id}: ${event.eventType}`);

        switch (event.eventType) {
            case "ORDER_CREATED":
                await this.handleOrderCreated(event.payload as OrderCreatedPayload);
                break;
            case "VOUCHERS_IMPORTED":
                await this.handleVouchersImported(event.payload as VouchersImportedPayload);
                break;
            default:
                console.log(`[FulfillmentConsumer] Unknown event type: ${event.eventType}`);
        }
    }

    /**
     * Handle ORDER_CREATED event - attempt to assign vouchers
     */
    private async handleOrderCreated(payload: OrderCreatedPayload): Promise<void> {
        console.log(`[FulfillmentConsumer] Processing order ${payload.orderId}`);

        const order = await ordersRepository.getOrderById(payload.orderId);
        if (!order) {
            console.error(`[FulfillmentConsumer] Order not found: ${payload.orderId}`);
            return;
        }

        if (order.status !== "PENDING_FULFILLMENT") {
            console.log(`[FulfillmentConsumer] Order ${payload.orderId} is not pending (${order.status})`);
            return;
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
            // TODO: Send push notification to user
        } else if (fulfilledCount > 0) {
            // Partially fulfilled - still pending
            console.log(
                `[FulfillmentConsumer] Order ${order.id} partially fulfilled (${fulfilledCount}/${order.quantity})`
            );
        } else {
            console.log(`[FulfillmentConsumer] No vouchers available for order ${order.id}`);
        }
    }

    /**
     * Handle VOUCHERS_IMPORTED event - backfill pending orders
     */
    private async handleVouchersImported(payload: VouchersImportedPayload): Promise<void> {
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
            } else if (fulfilledCount > 0) {
                console.log(`[FulfillmentConsumer] Order ${order.id} partially fulfilled (${fulfilledCount}/${order.quantity})`);
            } else {
                console.log(`[FulfillmentConsumer] No vouchers available for order ${order.id}`);
                break; // No more vouchers, stop processing
            }
        }
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
        return await db.transaction(async (tx) => {
            // Find oldest unassigned voucher matching criteria
            const [voucher] = await tx
                .select()
                .from(vouchers)
                .where(
                    and(
                        // Strict provider match (normalized to lowercase)
                        sql`lower(${vouchers.provider}) = lower(${provider})`,
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
