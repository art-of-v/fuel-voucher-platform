/**
 * Webhooks Controller
 * 
 * Handles Stripe webhook events for payment processing.
 * Migrated from legacy routes/webhooks.ts
 */

import { Router, Request, Response } from 'express';
import { paymentService } from '../../../services/payment.service';
import { ordersRepository } from '../../../features/orders/orders.repository';
import Stripe from 'stripe';
import { logger } from '../../../infrastructure/logging/logger';

const log = logger.child({ component: 'WebhooksController' });

/**
 * Controller for handling Stripe webhooks
 * IMPORTANT: This controller must receive raw body for signature verification
 */
export class WebhooksController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.setupRoutes();
    }

    private setupRoutes(): void {
        // POST /stripe - Handle Stripe webhook events
        this.router.post('/stripe', this.handleStripeWebhook.bind(this));
    }

    /**
     * POST /stripe
     * Handle Stripe webhook events
     * 
     * IMPORTANT: This route must use raw body, not JSON parsed body
     */
    private async handleStripeWebhook(req: Request, res: Response): Promise<void> {
        const signature = req.headers['stripe-signature'] as string;

        if (!signature) {
            res.status(400).send('Missing stripe-signature header');
            return;
        }

        try {
            // Verify webhook signature
            const rawBody = (req as any).rawBody;
            const event = paymentService.verifyWebhookSignature(rawBody || req.body, signature);

            log.info({ eventType: event.type }, 'Webhook received and verified');

            // Handle different event types
            switch (event.type) {
                case 'checkout.session.completed':
                    await this.handleCheckoutSessionCompleted(event);
                    break;

                case 'payment_intent.succeeded':
                    await this.handlePaymentIntentSucceeded(event);
                    break;

                case 'payment_intent.payment_failed':
                    await this.handlePaymentIntentFailed(event);
                    break;

                default:
                    log.info({ eventType: event.type }, 'Unhandled event type');
            }

            // Return 200 to acknowledge receipt
            res.json({ received: true });
        } catch (error: any) {
            log.error({ err: error }, 'Webhook error');
            res.status(400).send(`Webhook Error: ${error.message}`);
        }
    }

    /**
     * Handle checkout.session.completed event
     */
    private async handleCheckoutSessionCompleted(event: Stripe.Event): Promise<void> {
        const session = event.data.object as Stripe.Checkout.Session;

        if (session.payment_status === 'paid') {
            await paymentService.handleSuccessfulPayment(session);
            log.info({ sessionId: session.id }, 'Checkout session payment completed');
        }
    }

    /**
     * Handle payment_intent.succeeded event
     * Creates orders for voucher fulfillment
     */
    private async handlePaymentIntentSucceeded(event: Stripe.Event): Promise<void> {
        const paymentIntent = event.data.object as Stripe.PaymentIntent;

        log.info({
            paymentIntentId: paymentIntent.id,
            amount: paymentIntent.amount / 100,
            currency: 'UAH'
        }, 'PaymentIntent succeeded');

        // Get user ID from payment intent metadata
        const userId = paymentIntent.metadata.userId;
        const cartItems = paymentIntent.metadata.items;

        if (!userId || userId === 'guest' || !cartItems) {
            log.info('Skipping order creation - missing userId or cart items in metadata');
            return;
        }

        log.info({ userId, cartItems }, 'Processing order');

        // Parse cart items: "OKKO - A95 50L x1, WOG - Diesel 30L x2"
        const items = this.parseCartItems(cartItems);

        log.info({ parsedItems: items }, 'Parsed cart items');

        // Create orders for each cart item (async fulfillment via consumer)
        try {
            for (const item of items) {
                const productType = `${item.station}-${item.fuelType}-${item.liters}L`;
                const idempotencyKey = `${paymentIntent.id}-${productType}`;

                // Check for duplicate (idempotency)
                const existing = await ordersRepository.getOrderByIdempotencyKey(idempotencyKey);
                if (existing) {
                    log.info({ idempotencyKey }, 'Order already exists, skipping');
                    continue;
                }

                log.info({
                    userId,
                    productType,
                    quantity: item.quantity
                }, 'Creating order');

                const order = await ordersRepository.createOrderWithEvent({
                    userId,
                    productType,
                    provider: item.station,
                    fuelType: item.fuelType,
                    liters: item.liters,
                    quantity: item.quantity,
                    price: 0, // Price is in paymentIntent, could be extracted per-item if needed
                    status: 'PENDING_FULFILLMENT',
                    stripePaymentId: paymentIntent.id,
                    idempotencyKey,
                });

                log.info({ orderId: order.id }, 'Created order (async fulfillment pending)');
            }

            log.info({ userId }, 'Order creation complete');
        } catch (error: any) {
            log.error({ err: error }, 'Order creation failed');
        }
    }

    /**
     * Handle payment_intent.payment_failed event
     */
    private async handlePaymentIntentFailed(event: Stripe.Event): Promise<void> {
        const paymentIntent = event.data.object as Stripe.PaymentIntent;
        log.warn({ paymentIntentId: paymentIntent.id }, 'PaymentIntent failed');
        // Could implement retry logic or notification here
    }

    /**
     * Parse cart items string into structured objects
     * Format: "OKKO - A95 50L x1, WOG - Diesel 30L x2"
     */
    private parseCartItems(cartItems: string): Array<{
        station: string;
        fuelType: string;
        liters: number;
        quantity: number;
    }> {
        return cartItems.split(', ').map(item => {
            const match = item.match(/^(.+?)\s+-\s+(.+?)\s+(\d+)L\s+x(\d+)$/);
            if (match) {
                return {
                    station: match[1].trim(),
                    fuelType: match[2].trim(),
                    liters: parseInt(match[3]),
                    quantity: parseInt(match[4])
                };
            }
            return null;
        }).filter((item): item is NonNullable<typeof item> => item !== null);
    }
}
