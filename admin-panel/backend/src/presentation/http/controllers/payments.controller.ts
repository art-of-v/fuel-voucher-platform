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

        // DEV/PRODUCTION FALLBACK: Create order after payment if webhooks are not configured
        this.router.post('/create-order-after-payment', this.createOrderAfterPayment.bind(this));
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
     * POST /create-order-after-payment
     * Create orders after successful payment (fallback for when webhooks are not available)
     */
    private async createOrderAfterPayment(req: Request, res: Response): Promise<void> {
        try {
            // Get userId from body or session
            const session = (req as any).session;
            const sessionUserId = session?.userId || (req as any).user?.id;
            const userId = req.body.userId || sessionUserId;
            const { items } = req.body;

            if (!userId) {
                res.status(401).json({ error: 'Unauthorized: Missing user ID' });
                return;
            }

            if (!items || !Array.isArray(items)) {
                res.status(400).json({ error: 'Items array is required' });
                return;
            }

            log.info({ userId, itemCount: items.length }, 'Creating orders after payment');

            const createdOrders = [];

            for (const item of items) {
                const productType = `${item.station} - ${item.fuelType} ${item.liters}L`;
                // Use a more robust idempotency key that includes item details and timestamp (rounded to minute to avoid duplicates on refresh but allow new orders)
                const minuteTimestamp = Math.floor(Date.now() / 60000);
                const idempotencyKey = `orders-${userId}-${productType}-${item.quantity}-${minuteTimestamp}`;

                // Check if already exists to prevent duplicates
                const existing = await ordersRepository.getOrderByIdempotencyKey(idempotencyKey);
                if (existing) {
                    createdOrders.push(existing);
                    continue;
                }

                const newOrder = await ordersRepository.createOrderWithEvent({
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

                createdOrders.push(newOrder);
                log.info({ productType, orderId: newOrder.id }, 'Created PENDING order');
            }

            res.json({
                success: true,
                message: 'Orders created successfully',
                orders: createdOrders
            });
        } catch (error: any) {
            log.error({ err: error }, 'Error creating orders after payment');
            res.status(500).json({ error: error.message });
        }
    }
}
