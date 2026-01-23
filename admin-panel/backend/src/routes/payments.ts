import { Router, Request, Response } from 'express';
import { paymentService } from '../services/payment.service';
import { STRIPE_CONFIG } from '../config/stripe';
import { ordersRepository } from '../features/orders/orders.repository';

const router = Router();

/**
 * POST /api/payments/create-checkout-session
 * Create a Stripe Checkout Session
 */
router.post('/create-checkout-session', async (req: Request, res: Response) => {
    try {
        const { packageId, packageName, amount, quantity = 1 } = req.body;

        // Get user ID from session (adjust based on your auth implementation)
        const userId = (req as any).user?.id || 'guest';

        if (!packageId || !packageName || !amount) {
            return res.status(400).json({
                error: 'Missing required fields: packageId, packageName, amount',
            });
        }

        const session = await paymentService.createCheckoutSession({
            userId,
            packageId,
            packageName,
            amount,
            quantity,
        });

        res.json({
            sessionId: session.id,
            url: session.url,
        });
    } catch (error: any) {
        console.error('Error creating checkout session:', error);
        res.status(500).json({ error: error.message });
    }
});

/**
 * POST /api/payments/create-payment-intent
 * Create a Payment Intent for custom payment flows
 */
router.post('/create-payment-intent', async (req: Request, res: Response) => {
    try {
        const { amount, metadata } = req.body;

        // Get user ID from phone auth session or Replit auth
        const session = (req as any).session;
        const userId = session?.userId || (req as any).user?.id || 'guest';

        if (!amount) {
            return res.status(400).json({ error: 'Amount is required' });
        }

        const paymentIntent = await paymentService.createPaymentIntent({
            amount,
            userId,
            metadata: {
                ...metadata,
                userId, // Ensure userId is in metadata for webhook
            },
        });

        res.json({
            clientSecret: paymentIntent.client_secret,
            publishableKey: STRIPE_CONFIG.publishableKey,
        });
    } catch (error: any) {
        console.error('Error creating payment intent:', error);
        res.status(500).json({ error: error.message });
    }
});

/**
 * GET /api/payments/session/:sessionId
 * Retrieve checkout session details
 */
router.get('/session/:sessionId', async (req: Request, res: Response) => {
    try {
        const { sessionId } = req.params;
        const session = await paymentService.getCheckoutSession(sessionId);

        res.json({
            id: session.id,
            status: session.payment_status,
            amountTotal: session.amount_total,
            metadata: session.metadata,
        });
    } catch (error: any) {
        console.error('Error retrieving session:', error);
        res.status(500).json({ error: error.message });
    }
});

/**
 * GET /api/payments/config
 * Get Stripe publishable key for frontend
 */
router.get('/config', (req: Request, res: Response) => {
    res.json({
        publishableKey: STRIPE_CONFIG.publishableKey,
    });
});

/**
 * DEV ONLY: Simulate payment success
 * POST /api/payments/simulate-success-dev
 */
router.post('/simulate-success-dev', async (req: Request, res: Response) => {
    if (process.env.NODE_ENV === 'production') {
        return res.status(403).json({ error: 'Not available in production' });
    }

    try {
        const { userId, items } = req.body;

        if (!userId || !items || !Array.isArray(items)) {
            return res.status(400).json({ error: 'userId and items array required' });
        }

        console.log(`[DEV-PAYMENT] Simulating success for user: ${userId}`);

        for (const item of items) {
            const productType = `${item.station} - ${item.fuelType} ${item.liters}L`;
            const idempotencyKey = `dev-${Date.now()}-${productType}`;

            await ordersRepository.createOrderWithEvent({
                userId,
                productType,
                provider: item.station,
                fuelType: item.fuelType,
                liters: item.liters,
                quantity: item.quantity,
                price: item.price || 0,
                status: 'PENDING_FULFILLMENT',
                idempotencyKey,
            });
            console.log(`[DEV-PAYMENT] Created PENDING order for ${productType}`);
        }

        res.json({ success: true, message: 'Orders created in PENDING_FULFILLMENT status' });
    } catch (error: any) {
        console.error('Error simulating success:', error);
        res.status(500).json({ error: error.message });
    }
});

export default router;
