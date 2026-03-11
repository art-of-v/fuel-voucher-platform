/**
 * Sync Controller
 * 
 * Handles mobile app synchronization endpoints.
 */

import { Router, Request, Response, NextFunction } from 'express';
import { IOrderRepository } from '../../../domain/repositories/order.repository';
import { IVoucherRepository } from '../../../domain/repositories/voucher.repository';

export class SyncController {
    public readonly router: Router;

    constructor(
        private readonly orderRepository: IOrderRepository,
        private readonly voucherRepository: IVoucherRepository
    ) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.get('/', this.sync.bind(this));
        this.router.get('/orders', this.getOrders.bind(this));
    }

    /**
     * GET /api/sync
     * Sync endpoint for mobile app - returns user's orders and fulfilled vouchers
     */
    private async sync(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).userId;

            if (!userId) {
                res.status(401).json({ error: 'Unauthorized' });
                return;
            }

            // Fetch orders for user
            const orders = await this.orderRepository.findByUserId(userId);

            // Fetch fulfilled vouchers for user
            const vouchers = await this.voucherRepository.findByUserId(userId);

            // Get server timestamp for next sync
            const serverTimestamp = new Date().toISOString();

            res.json({
                orders: orders.map(order => ({
                    id: order.id,
                    productType: order.productType,
                    provider: order.provider,
                    fuelType: order.fuelType,
                    liters: order.liters,
                    quantity: order.quantity,
                    price: order.price,
                    status: order.status,
                    createdAt: order.createdAt?.toISOString(),
                    fulfilledAt: order.fulfilledAt?.toISOString() || null,
                })),
                vouchers: vouchers.map(voucher => ({
                    id: voucher.id,
                    provider: voucher.provider,
                    fuelType: voucher.fuelType,
                    amount: voucher.amount,
                    unit: voucher.unit,
                    status: voucher.status,
                    qrCodeData: voucher.qrCodeData,
                    externalId: voucher.externalId,
                    imageUrl: voucher.imageUrl,
                })),
                serverTimestamp,
            });
        } catch (error) {
            next(error);
        }
    }

    /**
     * GET /api/sync/orders
     * Get user's orders
     */
    private async getOrders(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).userId;

            if (!userId) {
                res.status(401).json({ error: 'Unauthorized' });
                return;
            }

            const orders = await this.orderRepository.findByUserId(userId);

            res.json(orders.map(order => ({
                id: order.id,
                productType: order.productType,
                provider: order.provider,
                fuelType: order.fuelType,
                liters: order.liters,
                quantity: order.quantity,
                price: order.price,
                status: order.status,
                createdAt: order.createdAt?.toISOString(),
                fulfilledAt: order.fulfilledAt?.toISOString() || null,
            })));
        } catch (error) {
            next(error);
        }
    }
}
