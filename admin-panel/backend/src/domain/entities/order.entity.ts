/**
 * Order Entity
 * 
 * Domain representation of an order with business logic.
 */

export type OrderStatus =
    | 'PENDING_FULFILLMENT'
    | 'PARTIALLY_FULFILLED'
    | 'FULFILLED'
    | 'REFUNDED'
    | 'CANCELLED';

export interface OrderProps {
    id: string;
    userId: string;
    productType: string;
    provider: string;
    fuelType: string;
    liters: number;
    quantity: number;
    price: number;
    status: OrderStatus;
    idempotencyKey: string | null;
    createdAt: Date;
    fulfilledAt: Date | null;
}

export class OrderEntity {
    private constructor(private readonly props: OrderProps) { }

    static create(props: OrderProps): OrderEntity {
        return new OrderEntity(props);
    }

    get id(): string {
        return this.props.id;
    }

    get userId(): string {
        return this.props.userId;
    }

    get productType(): string {
        return this.props.productType;
    }

    get provider(): string {
        return this.props.provider;
    }

    get fuelType(): string {
        return this.props.fuelType;
    }

    get liters(): number {
        return this.props.liters;
    }

    get quantity(): number {
        return this.props.quantity;
    }

    get price(): number {
        return this.props.price;
    }

    get status(): OrderStatus {
        return this.props.status;
    }

    get idempotencyKey(): string | null {
        return this.props.idempotencyKey;
    }

    get createdAt(): Date {
        return this.props.createdAt;
    }

    get fulfilledAt(): Date | null {
        return this.props.fulfilledAt;
    }

    isPending(): boolean {
        return this.props.status === 'PENDING_FULFILLMENT';
    }

    isFulfilled(): boolean {
        return this.props.status === 'FULFILLED';
    }

    isRefunded(): boolean {
        return this.props.status === 'REFUNDED';
    }

    isCancelled(): boolean {
        return this.props.status === 'CANCELLED';
    }

    canBeFulfilled(): boolean {
        return this.props.status === 'PENDING_FULFILLMENT' ||
            this.props.status === 'PARTIALLY_FULFILLED';
    }

    canBeRefunded(): boolean {
        return this.props.status === 'FULFILLED' ||
            this.props.status === 'PARTIALLY_FULFILLED';
    }

    getTotalLiters(): number {
        return this.props.liters * this.props.quantity;
    }

    toPlainObject(): OrderProps {
        return { ...this.props };
    }
}
