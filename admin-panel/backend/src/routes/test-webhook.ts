import { Router, Request, Response } from 'express';
import { vouchersRepository } from '../features/vouchers/vouchers.repository';
import { ordersRepository } from '../features/orders/orders.repository';
import { isRedisAvailable, publishToStream, STREAMS } from '../shared/infrastructure/redis';

const router = Router();

/**
 * TEST ENDPOINT - Manually trigger voucher assignment
 * POST /api/test/assign-vouchers
 * Body: { userId: string, items: [{ station: string, fuelType: string, liters: number, quantity: number }] }
 */
router.post('/assign-vouchers', async (req: Request, res: Response) => {
    try {
        const { userId, items } = req.body;

        if (!userId || !items || !Array.isArray(items)) {
            return res.status(400).json({
                error: 'userId and items array required',
                example: {
                    userId: 'a0e45e0e-b75b-43f4-abf7-f220c9ba7b59',
                    items: [
                        { station: 'OKKO', fuelType: 'A-95', liters: 10, quantity: 2 }
                    ]
                }
            });
        }

        console.log(`[TEST] Manually assigning vouchers to user: ${userId}`);
        const results = [];

        for (const item of items) {
            console.log(`[TEST] Assigning ${item.quantity}x ${item.station} ${item.fuelType} ${item.liters}L`);

            try {
                const assigned = await vouchersRepository.assignVouchersToPurchase(
                    0, // purchaseId
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
                    vouchers: assigned.map((v: any) => ({ id: v.id, provider: v.provider, fuelType: v.fuelType, amount: v.amount }))
                });

                console.log(`[TEST] Successfully assigned ${assigned.length} vouchers`);
            } catch (error: any) {
                console.error(`[TEST] Failed to assign vouchers:`, error.message);
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
    } catch (error: any) {
        console.error('[TEST] Error in manual voucher assignment:', error);
        res.status(500).json({ error: error.message });
    }
});

/**
 * TEST ENDPOINT - Trigger Voucher Import Event (for fulfillment backfill)
 * POST /api/test/trigger-import
 * Body: { provider: string, fuelType: string, liters: number }
 */
router.post('/trigger-import', async (req: Request, res: Response) => {
    try {
        const { provider, fuelType, liters } = req.body;

        const payload = {
            provider: provider || "OKKO",
            fuelType: fuelType || "DP EURO",
            liters: liters || 10,
            count: 1
        };

        console.log(`[TEST] Triggering VOUCHERS_IMPORTED event`, payload);

        // 1. DB Outbox (reliable)
        await ordersRepository.publishEvent("VOUCHERS_IMPORTED", payload);

        // 2. Redis Stream (real-time)
        if (await isRedisAvailable()) {
            await publishToStream(STREAMS.ORDER_EVENTS, "VOUCHERS_IMPORTED", payload);
        }

        res.json({ success: true, message: "VOUCHERS_IMPORTED event published", payload });
    } catch (error: any) {
        console.error('[TEST] Error triggering import event:', error);
        res.status(500).json({ error: error.message });
    }
});

/**
 * TEST ENDPOINT - Get user info
 * GET /api/test/user-info/:phone
 */
router.get('/user-info/:phone', async (req: Request, res: Response) => {
    try {
        const { phone } = req.params;
        // This would query the database for user info
        res.json({
            message: 'User info endpoint',
            phone,
            note: 'Query database to get user ID'
        });
    } catch (error: any) {
        res.status(500).json({ error: error.message });
    }
});

/**
 * TEST ENDPOINT - Simulate a complete purchase
 * POST /api/test/simulate-purchase
 * Body: { userId: string, provider: string, fuelType: string, liters: number, quantity: number }
 */
router.post('/simulate-purchase', async (req: Request, res: Response) => {
    try {
        const { userId, provider, fuelType, liters, quantity = 1 } = req.body;

        if (!userId || !provider || !fuelType || !liters) {
            return res.status(400).json({ error: "Missing fields" });
        }

        console.log(`[TEST] Simulating purchase for user: ${userId}`);

        // Create Order (EDA style - this handles Outbox + Redis automatically)
        const order = await ordersRepository.createOrderWithEvent({
            userId,
            provider,
            fuelType,
            liters,
            quantity,
            price: 50 * liters * quantity, // Dummy price
            productType: `${provider} ${fuelType} ${liters}L`,
            status: "PENDING_FULFILLMENT"
        });

        res.json({ success: true, orderId: order.id, message: "Purchase simulated and events published via repository" });
    } catch (error: any) {
        console.error('[TEST] Error simulating purchase:', error);
        res.status(500).json({ error: error.message });
    }
});

export default router;
