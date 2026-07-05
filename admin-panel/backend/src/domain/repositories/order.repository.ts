/**
 * Order Repository Interface
 * 
 * Defines persistence operations for Order entities.
 */

import { IBaseRepository } from './base.repository';

/**
 * Order status enum
 */
export type OrderStatus =
    | 'PENDING_FULFILLMENT'
    | 'PARTIALLY_FULFILLED'
    | 'FULFILLED'
    | 'REFUNDED'
    | 'CANCELLED';

/**
 * Order entity (domain representation)
 */
export interface Order {
    id: string;
    userId: string;
    productType: string;
    provider: string;
    fuelType: string;
    liters: number;
    quantity: number;
    price: number;
    status: OrderStatus;
    monobankInvoiceId: string | null;
    idempotencyKey: string | null;
    createdAt: Date;
    fulfilledAt: Date | null;
}

/**
 * Order creation data
 */
export interface CreateOrderData {
    userId: string;
    productType: string;
    provider: string;
    fuelType: string;
    liters: number;
    quantity: number;
    price: number;
    status?: OrderStatus;
    monobankInvoiceId?: string;
    idempotencyKey?: string;
}

/**
 * Order Repository Interface
 */
export interface IOrderRepository extends IBaseRepository<Order, string> {
    /**
     * Get order by idempotency key
     */
    findByIdempotencyKey(key: string): Promise<Order | null>;

    /**
     * Get all orders for a user
     */
    findByUserId(userId: string): Promise<Order[]>;

    /**
     * Get pending orders for a specific product type (FIFO)
     */
    findPendingByProductType(
        provider: string,
        fuelType: string,
        liters: number,
    ): Promise<Order[]>;

    /**
     * Get all pending orders
     */
    findAllPending(): Promise<Order[]>;

    /**
     * Create order with outbox event
     */
    createWithEvent(data: CreateOrderData): Promise<Order>;

    /**
     * Update order status
     */
    updateStatus(
        orderId: string,
        status: OrderStatus,
        fulfilledAt?: Date,
    ): Promise<void>;
}
