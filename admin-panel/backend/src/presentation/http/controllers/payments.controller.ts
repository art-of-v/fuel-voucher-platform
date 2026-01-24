/**
 * Payments Controller
 * 
 * Handles payment-related endpoints (Stripe checkout, payment intents).
 * Migrated from legacy routes/payments.ts
 */

import { Router, Request, Response } from 'express';
import { paymentService } from '../../../services/payment.service';
import { STRIPE_CONFIG } from '../../../config/stripe';
import { ordersRepository } from '../../../features/orders/orders.repository';
import { logger } from '../../../infrastructure/logging/logger';

const log = logger.child({ component: 'PaymentsController' });

/**
 * Controller for handling payment operations
 */
export class PaymentsController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.setupRoutes();
    }

    private setupRoutes(): void {
        // POST /create-checkout-session - Create Stripe Checkout Session
        this.router.post('/create-checkout-session', this.createCheckoutSession.bind(this));

        // POST /create-payment-intent - Create Payment Intent
        this.router.post('/create-payment-intent', this.createPaymentIntent.bind(this));

        // GET /session/:sessionId - Get checkout session details
        this.router.get('/session/:sessionId', this.getSession.bind(this));

        // GET /config - Get Stripe publishable key
        this.router.get('/config', this.getConfig.bind(this));

        // DEV ONLY: Simulate payment success
        this.router.post('/simulate-success-dev', this.simulateSuccessDev.bind(this));
    }

    /**
     * POST /create-checkout-session
     * Create a Stripe Checkout Session
     */
    private async createCheckoutSession(req: Request, res: Response): Promise<void> {
        try {
            const { packageId, packageName, amount, quantity = 1 } = req.body;

            // Get user ID from session (adjust based on your auth implementation)
            const userId = (req as any).user?.id || 'guest';

            if (!packageId || !packageName || !amount) {
                res.status(400).json({
                    error: 'Missing required fields: packageId, packageName, amount',
                });
                return;
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
            log.error({ err: error }, 'Error creating checkout session');
            res.status(500).json({ error: error.message });
        }
    }

    /**
     * POST /create-payment-intent
     * Create a Payment Intent for custom payment flows
     */
    private async createPaymentIntent(req: Request, res: Response): Promise<void> {
        try {
            const { amount, metadata } = req.body;

            // Get user ID from phone auth session or Replit auth
            const session = (req as any).session;
            const userId = session?.userId || (req as any).user?.id || 'guest';

            if (!amount) {
                res.status(400).json({ error: 'Amount is required' });
                return;
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
            log.error({ err: error }, 'Error creating payment intent');
            res.status(500).json({ error: error.message });
        }
    }

    /**
     * GET /session/:sessionId
     * Retrieve checkout session details
     */
    private async getSession(req: Request, res: Response): Promise<void> {
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
            log.error({ err: error }, 'Error retrieving session');
            res.status(500).json({ error: error.message });
        }
    }

    /**
     * GET /config
     * Get Stripe publishable key for frontend
     */
    private getConfig(_req: Request, res: Response): void {
        res.json({
            publishableKey: STRIPE_CONFIG.publishableKey,
        });
    }

    /**
     * DEV ONLY: Simulate payment success
     * POST /simulate-success-dev
     */
    private async simulateSuccessDev(req: Request, res: Response): Promise<void> {
        if (process.env.NODE_ENV === 'production') {
            res.status(403).json({ error: 'Not available in production' });
            return;
        }

        try {
            const { userId, items } = req.body;

            if (!userId || !items || !Array.isArray(items)) {
                res.status(400).json({ error: 'userId and items array required' });
                return;
            }

            log.info({ userId }, 'Simulating payment success (DEV)');

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

                log.info({ productType }, 'Created PENDING order (DEV)');
            }

            res.json({ success: true, message: 'Orders created in PENDING_FULFILLMENT status' });
        } catch (error: any) {
            log.error({ err: error }, 'Error simulating success');
            res.status(500).json({ error: error.message });
        }
    }
}
