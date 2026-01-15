import { Router, Request, Response } from 'express';
import { vouchersRepository } from '../features/vouchers/vouchers.repository';

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
                    vouchers: assigned.map(v => ({ id: v.id, provider: v.provider, fuelType: v.fuelType, amount: v.amount }))
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

export default router;
