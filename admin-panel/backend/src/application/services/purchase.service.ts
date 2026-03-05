/**
 * Purchase Service
 * 
 * Handles purchase creation, payment simulation, and order management.
 */

import { IOrderRepository } from '../../domain/repositories/order.repository';
import { IVoucherRepository } from '../../domain/repositories/voucher.repository';
import { AppError } from '../../shared/errors/app-error';
import { logger } from '../../infrastructure/logging/logger';

/**
 * Purchase repository interface (for legacy purchases table)
 */
export interface IPurchaseRepository {
    createPurchase(data: CreatePurchaseData): Promise<Purchase>;
    getPurchase(id: number): Promise<Purchase | null>;
    getPurchaseByMonobankInvoice(invoiceId: string): Promise<Purchase | null>;
    getPurchasesByUserId(userId: string): Promise<Purchase[]>;
    updatePurchaseStatus(id: number, status: string, monobankInvoiceId?: string, monobankStatus?: string, qrCodeId?: number, voucherId?: string): Promise<void>;
    getPurchaseWithQrCode(id: number): Promise<any>;
    getAllPurchases(): Promise<Purchase[]>;
}

export interface CreatePurchaseData {
    sessionId: string;
    packageId: string;
    stationId: string;
    stationName: string;
    fuelType: string;
    fuelName: string;
    liters: number;
    quantity: number;
    price: number;
    status?: string;
    monobankInvoiceId?: string;
    monobankStatus?: string;
}

export interface Purchase {
    id: number;
    sessionId: string;
    packageId: string;
    stationId: string;
    stationName: string;
    fuelType: string;
    fuelName: string;
    liters: number;
    quantity: number;
    price: number;
    qrCodeId: number | null;
    voucherId: string | null;
    status: string;
    monobankInvoiceId: string | null;
    monobankStatus: string | null;
    createdAt: Date;
}

export interface CheckoutRequest {
    packageId: string;
    stationId: string;
    stationName: string;
    fuelType: string;
    fuelName: string;
    liters: number;
    quantity: number;
    price: number;
}

export class PurchaseService {
    private readonly log = logger.child({ component: 'PurchaseService' });

    constructor(
        private readonly purchaseRepository: IPurchaseRepository,
        private readonly orderRepository: IOrderRepository,
        private readonly voucherRepository: IVoucherRepository,
    ) { }

    /**
     * Create a new checkout (purchase record)
     */
    async createCheckout(userId: string, request: CheckoutRequest): Promise<number> {
        this.log.info({ userId, request }, 'Creating checkout');

        const purchase = await this.purchaseRepository.createPurchase({
            sessionId: userId,
            packageId: request.packageId,
            stationId: request.stationId,
            stationName: request.stationName,
            fuelType: request.fuelType,
            fuelName: request.fuelName,
            liters: request.liters,
            quantity: request.quantity,
            price: request.price,
            status: 'pending',
        });

        this.log.info({ purchaseId: purchase.id }, 'Checkout created');

        return purchase.id;
    }

    /**
     * Simulate payment (dev mode)
     */
    async simulatePayment(purchaseId: number, scenario: 'success' | 'failure'): Promise<{ status: string; purchase?: Purchase }> {
        const purchase = await this.purchaseRepository.getPurchase(purchaseId);

        if (!purchase) {
            throw AppError.notFound('Purchase');
        }

        if (scenario === 'failure') {
            await this.purchaseRepository.updatePurchaseStatus(purchaseId, 'failed');
            return { status: 'failed' };
        }

        // Success scenario
        try {
            // 1. Mark purchase as paid
            await this.purchaseRepository.updatePurchaseStatus(purchaseId, 'paid');

            // 2. Create Order for Fulfillment (Async)
            // Split bulk purchase into individual orders for better tracking
            for (let i = 0; i < purchase.quantity; i++) {
                await this.orderRepository.createWithEvent({
                    userId: purchase.sessionId,
                    productType: `${purchase.stationName} ${purchase.fuelName} ${purchase.liters}L`,
                    provider: purchase.stationName,
                    fuelType: purchase.fuelName,
                    liters: purchase.liters,
                    quantity: 1,
                    price: purchase.price / purchase.quantity,
                    status: 'PENDING_FULFILLMENT',
                });
            }

            this.log.info({ purchaseId }, 'Payment simulated successfully, order created');

            // Return success with updated purchase info
            const updatedPurchase = await this.purchaseRepository.getPurchase(purchaseId);
            return { status: 'success', purchase: updatedPurchase! };

        } catch (err: any) {
            this.log.warn({ purchaseId, error: err.message }, 'Simulate payment business error');
            await this.purchaseRepository.updatePurchaseStatus(purchaseId, 'failed');
            throw AppError.conflict(err.message);
        }
    }

    /**
     * Complete a purchase (legacy flow)
     */
    async completePurchase(purchaseId: number): Promise<Purchase> {
        const purchase = await this.purchaseRepository.getPurchase(purchaseId);

        if (!purchase) {
            throw AppError.notFound('Purchase');
        }

        // Try to assign vouchers directly
        try {
            const assigned = await this.voucherRepository.assignToPurchase(
                purchaseId,
                purchase.sessionId,
                purchase.stationName,
                purchase.fuelName,
                purchase.liters,
                purchase.quantity
            );

            const voucherId = assigned.length > 0 ? assigned[0].id : undefined;
            await this.purchaseRepository.updatePurchaseStatus(purchaseId, 'delivered', undefined, voucherId);

            this.log.info({ purchaseId, voucherId }, 'Purchase completed with direct voucher assignment');

        } catch (err: any) {
            this.log.warn({ purchaseId, error: err.message }, 'Direct voucher assignment failed, creating pending order');

            // Create Order for Async Fulfillment
            // Split bulk purchase into individual orders for better tracking
            for (let i = 0; i < purchase.quantity; i++) {
                await this.orderRepository.createWithEvent({
                    userId: purchase.sessionId,
                    productType: `${purchase.stationName} ${purchase.fuelName} ${purchase.liters}L`,
                    provider: purchase.stationName,
                    fuelType: purchase.fuelName,
                    liters: purchase.liters,
                    quantity: 1,
                    price: purchase.price / purchase.quantity,
                    status: 'PENDING_FULFILLMENT',
                });
            }

            await this.purchaseRepository.updatePurchaseStatus(purchaseId, 'pending');
        }

        const finalizedPurchase = await this.purchaseRepository.getPurchase(purchaseId);
        return finalizedPurchase!;
    }

    /**
     * Get purchase by ID
     */
    async getPurchase(purchaseId: number): Promise<Purchase | null> {
        return this.purchaseRepository.getPurchase(purchaseId);
    }

    /**
     * Get user's purchases
     */
    async getUserPurchases(userId: string): Promise<any[]> {
        const purchases = await this.purchaseRepository.getPurchasesByUserId(userId);

        // Enrich with QR code data if applicable
        const enriched = await Promise.all(
            purchases.map(async (purchase) => {
                if ((purchase.qrCodeId || purchase.voucherId) && purchase.status === 'delivered') {
                    const data = await this.purchaseRepository.getPurchaseWithQrCode(purchase.id);
                    return data || purchase;
                }
                return purchase;
            })
        );

        return enriched;
    }

    /**
     * Get all purchases (admin)
     */
    async getAllPurchases(): Promise<Purchase[]> {
        return this.purchaseRepository.getAllPurchases();
    }

    /**
     * Get purchase by Monobank invoice ID
     */
    async getPurchaseByMonobankInvoice(invoiceId: string): Promise<Purchase | null> {
        return this.purchaseRepository.getPurchaseByMonobankInvoice(invoiceId);
    }

    /**
     * Update Monobank payment information
     */
    async updateMonobankInfo(purchaseId: number, invoiceId: string, status: string): Promise<void> {
        await this.purchaseRepository.updatePurchaseStatus(purchaseId, 'pending', invoiceId, status);
    }

    /**
     * Fulfill a purchase (called by webhook)
     */
    async fulfillPurchase(purchaseId: number | string): Promise<void> {
        const id = typeof purchaseId === 'string' ? parseInt(purchaseId, 10) : purchaseId;
        const purchase = await this.purchaseRepository.getPurchase(id);

        if (!purchase) {
            throw AppError.notFound('Purchase');
        }

        if (purchase.status === 'paid' || purchase.status === 'delivered') {
            return; // Already fulfilled
        }

        // 1. Mark purchase as paid
        await this.purchaseRepository.updatePurchaseStatus(id, 'paid');

        // 2. Create Orders for Fulfillment
        for (let i = 0; i < purchase.quantity; i++) {
            await this.orderRepository.createWithEvent({
                userId: purchase.sessionId,
                productType: `${purchase.stationName} ${purchase.fuelName} ${purchase.liters}L`,
                provider: purchase.stationName,
                fuelType: purchase.fuelName,
                liters: purchase.liters,
                quantity: 1,
                price: purchase.price / purchase.quantity,
                status: 'PENDING_FULFILLMENT',
            });
        }

        this.log.info({ purchaseId: id }, 'Purchase fulfilled via Monobank');
    }
}
