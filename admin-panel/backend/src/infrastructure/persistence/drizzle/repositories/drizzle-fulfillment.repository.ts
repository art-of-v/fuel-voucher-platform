/**
 * Drizzle Fulfillment Repository
 * 
 * Concrete implementation of IFulfillmentRepository using Drizzle ORM.
 */

import { db } from '../../../../shared/database/db';
import { eq, sql } from 'drizzle-orm';
import { fulfillments, orders, vouchers, type Fulfillment as DbFulfillment } from '../../../../shared/database/schema';
import type {
    IFulfillmentRepository,
    Fulfillment,
    CreateFulfillmentData,
    FulfillmentWithDetails
} from '../../../../domain/repositories/fulfillment.repository';
import type { PaginatedResult, PaginationOptions } from '../../../../domain/repositories/base.repository';

function mapToDomain(dbFulfillment: DbFulfillment): Fulfillment {
    return {
        id: dbFulfillment.id,
        orderId: dbFulfillment.orderId,
        voucherId: dbFulfillment.voucherId,
        quantity: 1,
        fulfilledAt: dbFulfillment.fulfilledAt,
    };
}

export class DrizzleFulfillmentRepository implements IFulfillmentRepository {
    async findById(id: number): Promise<Fulfillment | null> {
        const [fulfillment] = await db.select().from(fulfillments).where(eq(fulfillments.id, id));
        return fulfillment ? mapToDomain(fulfillment) : null;
    }

    async findByOrderId(orderId: string): Promise<Fulfillment[]> {
        const result = await db
            .select()
            .from(fulfillments)
            .where(eq(fulfillments.orderId, orderId));
        return result.map(mapToDomain);
    }

    async findByVoucherId(voucherId: string): Promise<Fulfillment[]> {
        const result = await db
            .select()
            .from(fulfillments)
            .where(eq(fulfillments.voucherId, voucherId));
        return result.map(mapToDomain);
    }

    async findWithDetails(
        pagination?: PaginationOptions,
    ): Promise<PaginatedResult<FulfillmentWithDetails>> {
        let query = db
            .select({
                id: fulfillments.id,
                orderId: fulfillments.orderId,
                voucherId: fulfillments.voucherId,
                fulfilledAt: fulfillments.fulfilledAt,
                orderUserId: orders.userId,
                orderProductType: orders.productType,
                orderStatus: orders.status,
                voucherProvider: vouchers.provider,
                voucherFuelType: vouchers.fuelType,
                voucherAmount: vouchers.amount,
            })
            .from(fulfillments)
            .leftJoin(orders, eq(fulfillments.orderId, orders.id))
            .leftJoin(vouchers, eq(fulfillments.voucherId, vouchers.id));

        if (pagination) {
            query = query.limit(pagination.limit).offset(pagination.offset) as any;
        }

        const result = await query;

        const totalResult = await db
            .select({ count: sql<number>`count(*)` })
            .from(fulfillments);
        const total = Number(totalResult[0]?.count ?? 0);

        const data: FulfillmentWithDetails[] = result.map((r: any) => ({
            id: r.id,
            orderId: r.orderId,
            voucherId: r.voucherId,
            quantity: 1,
            fulfilledAt: r.fulfilledAt,
            order: r.orderUserId ? {
                id: r.orderId,
                userId: r.orderUserId,
                productType: r.orderProductType!,
                status: r.orderStatus!,
            } : undefined,
            voucher: r.voucherProvider ? {
                id: r.voucherId,
                provider: r.voucherProvider,
                fuelType: r.voucherFuelType!,
                amount: r.voucherAmount!,
            } : undefined,
        }));

        return {
            data,
            total,
            limit: pagination?.limit ?? total,
            offset: pagination?.offset ?? 0,
        };
    }

    async create(data: CreateFulfillmentData): Promise<Fulfillment> {
        const [fulfillment] = await db.insert(fulfillments).values({
            orderId: data.orderId,
            voucherId: data.voucherId,
        }).returning();
        return mapToDomain(fulfillment);
    }

    async getTotalFulfilledForOrder(orderId: string): Promise<number> {
        const result = await db
            .select({ count: sql<number>`count(*)` })
            .from(fulfillments)
            .where(eq(fulfillments.orderId, orderId));
        return Number(result[0]?.count ?? 0);
    }

    async findAll(): Promise<Fulfillment[]> {
        const result = await db.select().from(fulfillments);
        return result.map(mapToDomain);
    }

    async save(entity: Fulfillment): Promise<Fulfillment> {
        return entity;
    }

    async delete(id: number): Promise<void> {
        await db.delete(fulfillments).where(eq(fulfillments.id, id));
    }

    async count(): Promise<number> {
        const result = await db.select().from(fulfillments);
        return result.length;
    }

    async exists(id: number): Promise<boolean> {
        const [fulfillment] = await db.select({ id: fulfillments.id }).from(fulfillments).where(eq(fulfillments.id, id));
        return !!fulfillment;
    }
}

export const drizzleFulfillmentRepository = new DrizzleFulfillmentRepository();
