import { Router, Request, Response } from 'express';
import { paymentService } from '../services/payment.service';
import { STRIPE_CONFIG } from '../config/stripe';

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
        const { amount } = req.body;

        // Get user ID from phone auth session or Replit auth
        const session = (req as any).session;
        const userId = session?.userId || (req as any).user?.id || 'guest';

        if (!amount) {
            return res.status(400).json({ error: 'Amount is required' });
        }

        const paymentIntent = await paymentService.createPaymentIntent({
            amount,
            userId,
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

export default router;
