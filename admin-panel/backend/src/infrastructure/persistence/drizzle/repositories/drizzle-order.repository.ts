/**
 * Drizzle Order Repository
 * 
 * Concrete implementation of IOrderRepository using Drizzle ORM.
 */

import { db } from '../../../../shared/database/db';
import { eq, and, asc, inArray } from 'drizzle-orm';
import { orders, outbox, type Order as DbOrder } from '../../../../shared/database/schema';
import type {
    IOrderRepository,
    Order,
    OrderStatus,
    CreateOrderData
} from '../../../../domain/repositories/order.repository';
import { getFuelAliases } from '../../../../domain/services/fuel-matcher.service';
import { isRedisAvailable, publishToStream, STREAMS } from '../../../../shared/infrastructure/redis';

/**
 * Map database order to domain order
 */
function mapToDomain(dbOrder: DbOrder): Order {
    return {
        id: dbOrder.id,
        userId: dbOrder.userId,
        productType: dbOrder.productType,
        provider: dbOrder.provider,
        fuelType: dbOrder.fuelType,
        liters: dbOrder.liters,
        quantity: dbOrder.quantity,
        price: dbOrder.price,
        status: dbOrder.status as OrderStatus,
        stripePaymentId: dbOrder.stripePaymentId,
        idempotencyKey: dbOrder.idempotencyKey,
        createdAt: dbOrder.createdAt,
        fulfilledAt: dbOrder.fulfilledAt,
    };
}

export class DrizzleOrderRepository implements IOrderRepository {
    async findById(id: string): Promise<Order | null> {
        const [order] = await db.select().from(orders).where(eq(orders.id, id));
        return order ? mapToDomain(order) : null;
    }

    async findByIdempotencyKey(key: string): Promise<Order | null> {
        const [order] = await db
            .select()
            .from(orders)
            .where(eq(orders.idempotencyKey, key));
        return order ? mapToDomain(order) : null;
    }

    async findByUserId(userId: string): Promise<Order[]> {
        const result = await db
            .select()
            .from(orders)
            .where(eq(orders.userId, userId))
            .orderBy(asc(orders.createdAt));
        return result.map(mapToDomain);
    }

    async findPendingByProductType(
        provider: string,
        fuelType: string,
        liters: number,
    ): Promise<Order[]> {
        const aliases = getFuelAliases(fuelType);

        const result = await db
            .select()
            .from(orders)
            .where(
                and(
                    eq(orders.status, 'PENDING_FULFILLMENT'),
                    eq(orders.provider, provider),
                    inArray(orders.fuelType, aliases),
                    eq(orders.liters, liters)
                )
            )
            .orderBy(asc(orders.createdAt));

        return result.map(mapToDomain);
    }

    async findAllPending(): Promise<Order[]> {
        const result = await db
            .select()
            .from(orders)
            .where(eq(orders.status, 'PENDING_FULFILLMENT'))
            .orderBy(asc(orders.createdAt));
        return result.map(mapToDomain);
    }

    async findAll(): Promise<Order[]> {
        const result = await db.select().from(orders);
        return result.map(mapToDomain);
    }

    async create(data: CreateOrderData): Promise<Order> {
        const [order] = await db.insert(orders).values({
            userId: data.userId,
            productType: data.productType,
            provider: data.provider,
            fuelType: data.fuelType,
            liters: data.liters,
            quantity: data.quantity,
            price: data.price,
            status: data.status ?? 'PENDING_FULFILLMENT',
            stripePaymentId: data.stripePaymentId,
            idempotencyKey: data.idempotencyKey,
        }).returning();
        return mapToDomain(order);
    }

    async createWithEvent(data: CreateOrderData): Promise<Order> {
        const created = await db.transaction(async (tx: any) => {
            const [newOrder] = await tx.insert(orders).values({
                userId: data.userId,
                productType: data.productType,
                provider: data.provider,
                fuelType: data.fuelType,
                liters: data.liters,
                quantity: data.quantity,
                price: data.price,
                status: data.status ?? 'PENDING_FULFILLMENT',
                stripePaymentId: data.stripePaymentId,
                idempotencyKey: data.idempotencyKey,
            }).returning();

            await tx.insert(outbox).values({
                eventType: 'ORDER_CREATED',
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

        try {
            if (await isRedisAvailable()) {
                await publishToStream(STREAMS.ORDER_EVENTS, 'ORDER_CREATED', {
                    orderId: created.id,
                    userId: created.userId,
                    provider: created.provider,
                    fuelType: created.fuelType,
                    liters: created.liters,
                    quantity: created.quantity,
                });
            }
        } catch (err: any) {
            console.warn(`[OrderRepository] Failed to publish to Redis: ${err.message}`);
        }

        return mapToDomain(created);
    }

    async updateStatus(
        orderId: string,
        status: OrderStatus,
        fulfilledAt?: Date,
    ): Promise<void> {
        const updates: Record<string, any> = { status };
        if (fulfilledAt) {
            updates.fulfilledAt = fulfilledAt;
        }
        await db.update(orders).set(updates).where(eq(orders.id, orderId));
    }

    async update(id: string, data: Partial<Order>): Promise<Order> {
        const [order] = await db
            .update(orders)
            .set(data)
            .where(eq(orders.id, id))
            .returning();
        return mapToDomain(order);
    }

    async save(entity: Order): Promise<Order> {
        return this.update(entity.id, entity);
    }

    async delete(id: string): Promise<void> {
        await db.delete(orders).where(eq(orders.id, id));
    }

    async count(): Promise<number> {
        const result = await db.select().from(orders);
        return result.length;
    }

    async exists(id: string): Promise<boolean> {
        const [order] = await db.select({ id: orders.id }).from(orders).where(eq(orders.id, id));
        return !!order;
    }
}

export const drizzleOrderRepository = new DrizzleOrderRepository();
