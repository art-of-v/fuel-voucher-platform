
import { describe, it, expect } from 'vitest';
import { OrderEntity, OrderProps, OrderStatus } from '../../src/domain/entities/order.entity';

describe('Order Entity', () => {
    const defaultProps: OrderProps = {
        id: 'order-123',
        userId: 'user-456',
        productType: 'OKKO A-95',
        provider: 'OKKO',
        fuelType: 'A-95',
        liters: 20,
        quantity: 1,
        price: 1000,
        status: 'PENDING_FULFILLMENT',
        idempotencyKey: 'idem_123',
        createdAt: new Date('2023-01-01T10:00:00Z'),
        fulfilledAt: null,
    };

    it('should create an order entity correctly', () => {
        const order = OrderEntity.create(defaultProps);
        expect(order).toBeInstanceOf(OrderEntity);
        expect(order.id).toBe(defaultProps.id);
        expect(order.userId).toBe(defaultProps.userId);
        expect(order.status).toBe('PENDING_FULFILLMENT');
    });

    it('should calculate total liters', () => {
        const order = OrderEntity.create({ ...defaultProps, liters: 10, quantity: 3 });
        expect(order.getTotalLiters()).toBe(30);
    });

    it('should check if pending', () => {
        const pendingOrder = OrderEntity.create({ ...defaultProps, status: 'PENDING_FULFILLMENT' });
        expect(pendingOrder.isPending()).toBe(true);

        const fulfilledOrder = OrderEntity.create({ ...defaultProps, status: 'FULFILLED' });
        expect(fulfilledOrder.isPending()).toBe(false);
    });

    it('should check if fulfilled', () => {
        const fulfilledOrder = OrderEntity.create({ ...defaultProps, status: 'FULFILLED' });
        expect(fulfilledOrder.isFulfilled()).toBe(true);
    });

    it('should check if refunded', () => {
        const refundedOrder = OrderEntity.create({ ...defaultProps, status: 'REFUNDED' });
        expect(refundedOrder.isRefunded()).toBe(true);
    });

    it('should check if cancelled', () => {
        const cancelledOrder = OrderEntity.create({ ...defaultProps, status: 'CANCELLED' });
        expect(cancelledOrder.isCancelled()).toBe(true);
    });

    it('should check fulfillability', () => {
        const pending = OrderEntity.create({ ...defaultProps, status: 'PENDING_FULFILLMENT' });
        expect(pending.canBeFulfilled()).toBe(true);

        const partial = OrderEntity.create({ ...defaultProps, status: 'PARTIALLY_FULFILLED' });
        expect(partial.canBeFulfilled()).toBe(true);

        const fulfilled = OrderEntity.create({ ...defaultProps, status: 'FULFILLED' });
        expect(fulfilled.canBeFulfilled()).toBe(false);

        const refunded = OrderEntity.create({ ...defaultProps, status: 'REFUNDED' });
        expect(refunded.canBeFulfilled()).toBe(false);
    });

    it('should check refundability', () => {
        const fulfilled = OrderEntity.create({ ...defaultProps, status: 'FULFILLED' });
        expect(fulfilled.canBeRefunded()).toBe(true);

        const partial = OrderEntity.create({ ...defaultProps, status: 'PARTIALLY_FULFILLED' });
        expect(partial.canBeRefunded()).toBe(true);

        const pending = OrderEntity.create({ ...defaultProps, status: 'PENDING_FULFILLMENT' });
        expect(pending.canBeRefunded()).toBe(false);
    });

    it('should return plain object representation', () => {
        const order = OrderEntity.create(defaultProps);
        expect(order.toPlainObject()).toEqual(defaultProps);
    });

    it('should expose all properties via getters', () => {
        const order = OrderEntity.create(defaultProps);
        expect(order.id).toBe(defaultProps.id);
        expect(order.userId).toBe(defaultProps.userId);
        expect(order.productType).toBe(defaultProps.productType);
        expect(order.provider).toBe(defaultProps.provider);
        expect(order.fuelType).toBe(defaultProps.fuelType);
        expect(order.liters).toBe(defaultProps.liters);
        expect(order.quantity).toBe(defaultProps.quantity);
        expect(order.price).toBe(defaultProps.price);
        expect(order.status).toBe(defaultProps.status);
        expect(order.idempotencyKey).toBe(defaultProps.idempotencyKey);
        expect(order.createdAt).toEqual(defaultProps.createdAt);
        expect(order.fulfilledAt).toEqual(defaultProps.fulfilledAt);
    });
});
