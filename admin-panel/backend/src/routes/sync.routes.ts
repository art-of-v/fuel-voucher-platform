import { Router, Request, Response } from 'express';
import { ordersRepository } from '../features/orders/orders.repository';
import { vouchersRepository } from '../features/vouchers/vouchers.repository';

const router = Router();

/**
 * GET /api/sync
 * Sync endpoint for mobile app - returns user's orders and fulfilled vouchers
 */
router.get('/', async (req: Request, res: Response) => {
    try {
        // Get user ID from session
        const userId = (req.session as any)?.passport?.user;

        if (!userId) {
            return res.status(401).json({ error: 'Unauthorized' });
        }

        // Get timestamp from query for delta sync
        const sinceParam = req.query.since as string;
        const since = sinceParam ? new Date(sinceParam) : new Date(0);

        // Fetch orders for user
        const orders = await ordersRepository.getOrdersByUserId(userId);

        // Fetch fulfilled vouchers for user
        const vouchers = await vouchersRepository.getUserVouchers(userId);

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
                qrCodeUrl: voucher.qrCodeUrl,
                qrCodeData: voucher.qrCodeData,
                externalId: voucher.externalId,
            })),
            serverTimestamp,
        });
    } catch (error: any) {
        console.error('[SYNC] Error:', error.message);
        res.status(500).json({ error: 'Sync failed' });
    }
});

/**
 * GET /api/sync/orders
 * Get user's orders
 */
router.get('/orders', async (req: Request, res: Response) => {
    try {
        const userId = (req.session as any)?.passport?.user;

        if (!userId) {
            return res.status(401).json({ error: 'Unauthorized' });
        }

        const orders = await ordersRepository.getOrdersByUserId(userId);

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
    } catch (error: any) {
        console.error('[SYNC] Orders error:', error.message);
        res.status(500).json({ error: 'Failed to fetch orders' });
    }
});

export default router;
