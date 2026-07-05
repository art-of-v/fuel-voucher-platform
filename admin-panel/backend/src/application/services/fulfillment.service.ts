/**
 * Fulfillment Service
 * 
 * Handles order fulfillment by assigning vouchers to orders.
 */

import { db, type DbTransaction } from '../../shared/database/db';
import { eq, and, asc, inArray, or } from 'drizzle-orm';
import { vouchers, fulfillments } from '../../shared/database/schema';
import { IOrderRepository } from '../../domain/repositories/order.repository';
import { getFuelAliases } from '../../domain/services/fuel-matcher.service';
import { logger } from '../../infrastructure/logging/logger';

export interface OrderCreatedPayload {
    orderId: string;
    userId: string;
    provider: string;
    fuelType: string;
    liters: number;
    quantity: number;
}

export interface VouchersImportedPayload {
    provider: string;
    fuelType: string;
    liters: number;
    count: number;
}

export class FulfillmentService {
    private readonly log = logger.child({ component: 'FulfillmentService' });

    constructor(
        private readonly orderRepository: IOrderRepository,
    ) { }

    /**
     * Handle ORDER_CREATED event - attempt to assign vouchers
     */
    async handleOrderCreated(payload: OrderCreatedPayload): Promise<void> {
        const { orderId, userId, provider, fuelType, liters, quantity } = payload;
        this.log.info({ orderId, provider, fuelType, liters }, 'Processing ORDER_CREATED event');

        let assignedCount = 0;

        for (let i = 0; i < quantity; i++) {
            const assigned = await this.findAndAssignVoucher(orderId, userId, provider, fuelType, liters);
            if (assigned) {
                assignedCount++;
            } else {
                break; // No more vouchers available
            }
        }

        // Update order status based on fulfillment
        if (assignedCount === quantity) {
            await this.orderRepository.updateStatus(orderId, 'FULFILLED', new Date());
            this.log.info({ orderId, assignedCount }, 'Order fully fulfilled');
        } else if (assignedCount > 0) {
            await this.orderRepository.updateStatus(orderId, 'PARTIALLY_FULFILLED');
            this.log.info({ orderId, assignedCount, total: quantity }, 'Order partially fulfilled');
        } else {
            this.log.info({ orderId }, 'No vouchers available for order');
        }
    }

    /**
     * Handle VOUCHERS_IMPORTED event - backfill pending orders
     */
    async handleVouchersImported(payload: VouchersImportedPayload): Promise<void> {
        const { provider, fuelType, liters, count } = payload;
        this.log.info({ provider, fuelType, liters, count }, 'Processing VOUCHERS_IMPORTED event');

        // Get pending orders that match this voucher type (FIFO)
        const pendingOrders = await this.orderRepository.findPendingByProductType(provider, fuelType, liters);

        this.log.info({ pendingOrdersCount: pendingOrders.length }, 'Found pending orders for backfill');

        for (const order of pendingOrders) {
            // Check how many vouchers this order still needs
            const fulfilledCount = await this.getFulfilledCount(order.id);
            const remaining = order.quantity - fulfilledCount;

            if (remaining <= 0) continue;

            let assignedCount = 0;
            for (let i = 0; i < remaining; i++) {
                const assigned = await this.findAndAssignVoucher(
                    order.id,
                    order.userId,
                    order.provider,
                    order.fuelType,
                    order.liters
                );
                if (assigned) {
                    assignedCount++;
                } else {
                    break;
                }
            }

            // Update order status
            const newFulfilledCount = fulfilledCount + assignedCount;
            if (newFulfilledCount >= order.quantity) {
                await this.orderRepository.updateStatus(order.id, 'FULFILLED', new Date());
                this.log.info({ orderId: order.id }, 'Order fulfilled via backfill');
            } else if (newFulfilledCount > fulfilledCount) {
                await this.orderRepository.updateStatus(order.id, 'PARTIALLY_FULFILLED');
                this.log.info({ orderId: order.id, fulfilled: newFulfilledCount, total: order.quantity }, 'Order partially fulfilled via backfill');
            }
        }
    }

    /**
     * Find an available voucher and assign it to an order
     */
    async findAndAssignVoucher(
        orderId: string,
        userId: string,
        provider: string,
        fuelType: string,
        liters: number
    ): Promise<boolean> {
        const aliases = getFuelAliases(fuelType);

        return await db.transaction(async (tx: DbTransaction) => {
            // Find and lock available voucher
            const [voucher] = await tx
                .select()
                .from(vouchers)
                .where(
                    and(
                        eq(vouchers.provider, provider),
                        inArray(vouchers.fuelType, aliases),
                        eq(vouchers.amount, liters),
                        or(
                            eq(vouchers.status, 'available'),
                            eq(vouchers.status, 'imported')
                        )
                    )
                )
                .orderBy(asc(vouchers.createdAt))
                .limit(1)
                .for('update');

            if (!voucher) {
                return false;
            }

            // Update voucher status
            await tx
                .update(vouchers)
                .set({
                    status: 'sold',
                    assignedToUserId: userId,
                    updatedAt: new Date(),
                })
                .where(eq(vouchers.id, voucher.id));

            // Create fulfillment record
            await tx.insert(fulfillments).values({
                orderId,
                voucherId: voucher.id,
            });

            this.log.debug({ orderId, voucherId: voucher.id }, 'Voucher assigned to order');

            return true;
        });
    }

    /**
     * Get fulfilled count for an order
     */
    private async getFulfilledCount(orderId: string): Promise<number> {
        const result = await db
            .select()
            .from(fulfillments)
            .where(eq(fulfillments.orderId, orderId));
        return result.length;
    }

    /**
     * Process a single event
     */
    async processEvent(event: { id: number | string; eventType: string; payload: unknown }): Promise<void> {
        this.log.debug({ eventId: event.id, eventType: event.eventType }, 'Processing event');

        switch (event.eventType) {
            case 'ORDER_CREATED':
                await this.handleOrderCreated(event.payload as OrderCreatedPayload);
                break;
            case 'VOUCHERS_IMPORTED':
                await this.handleVouchersImported(event.payload as VouchersImportedPayload);
                break;
            default:
                this.log.warn({ eventType: event.eventType }, 'Unknown event type');
        }
    }
}
