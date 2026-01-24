/**
 * Fulfillment Repository Interface
 * 
 * Defines persistence operations for Fulfillment entities.
 */

import { IBaseRepository, PaginatedResult, PaginationOptions } from './base.repository';

/**
 * Fulfillment entity (domain representation)
 */
export interface Fulfillment {
    id: number;
    orderId: string;
    voucherId: string;
    quantity: number;
    fulfilledAt: Date;
}

/**
 * Fulfillment creation data
 */
export interface CreateFulfillmentData {
    orderId: string;
    voucherId: string;
    quantity: number;
}

/**
 * Fulfillment with related data
 */
export interface FulfillmentWithDetails extends Fulfillment {
    order?: {
        id: string;
        userId: string;
        productType: string;
        status: string;
    };
    voucher?: {
        id: string;
        provider: string;
        fuelType: string;
        amount: number;
    };
}

/**
 * Fulfillment Repository Interface
 */
export interface IFulfillmentRepository extends IBaseRepository<Fulfillment, number> {
    /**
     * Get fulfillments for an order
     */
    findByOrderId(orderId: string): Promise<Fulfillment[]>;

    /**
     * Get fulfillments for a voucher
     */
    findByVoucherId(voucherId: string): Promise<Fulfillment[]>;

    /**
     * Get fulfillments with details (joins)
     */
    findWithDetails(
        pagination?: PaginationOptions,
    ): Promise<PaginatedResult<FulfillmentWithDetails>>;

    /**
     * Create fulfillment record
     */
    create(data: CreateFulfillmentData): Promise<Fulfillment>;

    /**
     * Get total fulfilled quantity for an order
     */
    getTotalFulfilledForOrder(orderId: string): Promise<number>;
}
