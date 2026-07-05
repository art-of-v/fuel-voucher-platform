/**
 * Test Webhook Controller
 * 
 * Test endpoints for manual voucher assignment and event triggering.
 * DEV/STAGING ONLY.
 */

import { Router, Request, Response, NextFunction } from 'express';
import { IVoucherRepository } from '../../../domain/repositories/voucher.repository';
import { IOrderRepository } from '../../../domain/repositories/order.repository';
import { isRedisAvailable, publishToStream, STREAMS } from '../../../shared/infrastructure/redis';

export class TestWebhookController {
    public readonly router: Router;

    constructor(
        private readonly voucherRepository: IVoucherRepository,
        private readonly orderRepository: IOrderRepository
    ) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        this.router.post('/assign-vouchers', this.assignVouchers.bind(this));
        this.router.post('/trigger-order-created', this.triggerOrderCreated.bind(this));
    }

    /**
     * POST /api/test/assign-vouchers
     * Manually trigger voucher assignment
     */
    private async assignVouchers(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { userId, items } = req.body;

            if (!userId || !items || !Array.isArray(items)) {
                res.status(400).json({
                    error: 'userId and items array required',
                    example: {
                        userId: 'a0e45e0e-b75b-43f4-abf7-f220c9ba7b59',
                        items: [
                            { station: 'OKKO', fuelType: 'A-95', liters: 10, quantity: 2 }
                        ]
                    }
                });
                return;
            }

            const results = [];

            for (const item of items) {
                try {
                    // Use assignToPurchase method
                    const assigned = await this.voucherRepository.assignToPurchase(
                        0, // purchaseId (test)
                        userId,
                        item.station,
                        item.fuelType,
                        item.liters,
                        item.quantity
                    );

                    results.push({
                        success: true,
                        item,
                        assigned: assigned.length,
                        vouchers: assigned.map(v => ({
                            id: v.id,
                            provider: v.provider,
                            fuelType: v.fuelType,
                            amount: v.amount
                        }))
                    });
                } catch (error: any) {
                    results.push({
                        success: false,
                        item,
                        error: error.message
                    });
                }
            }

            res.json({
                success: true,
                userId,
                results
            });
        } catch (error) {
            next(error);
        }
    }

    /**
     * POST /api/test/trigger-order-created
     * Trigger order created event for fulfillment testing
     */
    private async triggerOrderCreated(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { orderId } = req.body;

            if (!orderId) {
                res.status(400).json({
                    error: 'orderId required',
                    example: { orderId: 'a0e45e0e-b75b-43f4-abf7-f220c9ba7b59' }
                });
                return;
            }

            const order = await this.orderRepository.findById(orderId);

            if (!order) {
                res.status(404).json({ error: 'Order not found' });
                return;
            }

            if (!isRedisAvailable()) {
                res.status(503).json({ error: 'Redis not available' });
                return;
            }

            // Publish to ORDER_EVENTS stream
            await publishToStream(
                STREAMS.ORDER_EVENTS,
                'ORDER_CREATED',
                {
                    orderId: order.id,
                    userId: order.userId,
                    provider: order.provider,
                    fuelType: order.fuelType,
                    liters: order.liters,
                    quantity: order.quantity,
                    timestamp: new Date().toISOString()
                }
            );

            res.json({
                success: true,
                message: 'ORDER_CREATED event published',
                order: {
                    id: order.id,
                    userId: order.userId,
                    provider: order.provider,
                    fuelType: order.fuelType,
                    liters: order.liters,
                    quantity: order.quantity
                }
            });
        } catch (error) {
            next(error);
        }
    }
}
