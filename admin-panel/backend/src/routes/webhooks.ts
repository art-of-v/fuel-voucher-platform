import { Router, Request, Response } from 'express';
import { paymentService } from '../services/payment.service';
import { ordersRepository } from '../features/orders/orders.repository';
import Stripe from 'stripe';

const router = Router();

/**
 * POST /api/webhooks/stripe
 * Handle Stripe webhook events
 * 
 * IMPORTANT: This route must use raw body, not JSON parsed body
 */
router.post(
    '/stripe',
    async (req: Request, res: Response) => {
        const signature = req.headers['stripe-signature'] as string;

        if (!signature) {
            return res.status(400).send('Missing stripe-signature header');
        }

        try {
            // Verify webhook signature
            const rawBody = (req as any).rawBody;
            const event = paymentService.verifyWebhookSignature(rawBody || req.body, signature);
            console.log(`[WEBHOOK] Received and verified: ${event.type}`);

            console.log('Webhook received:', event.type);

            // Handle different event types
            switch (event.type) {
                case 'checkout.session.completed': {
                    const session = event.data.object as Stripe.Checkout.Session;

                    if (session.payment_status === 'paid') {
                        await paymentService.handleSuccessfulPayment(session);
                    }
                    break;
                }

                case 'payment_intent.succeeded': {
                    const paymentIntent = event.data.object as Stripe.PaymentIntent;
                    console.log('PaymentIntent succeeded:', paymentIntent.id);
                    console.log('Payment metadata:', paymentIntent.metadata);
                    console.log('Payment amount:', paymentIntent.amount / 100, 'UAH');

                    // Get user ID from payment intent metadata
                    const userId = paymentIntent.metadata.userId;
                    const cartItems = paymentIntent.metadata.items;

                    if (userId && userId !== 'guest' && cartItems) {
                        console.log(`[WEBHOOK] Processing order for user: ${userId}`);
                        console.log(`[WEBHOOK] Cart items summary: ${cartItems}`);

                        // Parse cart items: "OKKO - A95 50L x1, WOG - Diesel 30L x2"
                        const items = cartItems.split(', ').map(item => {
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
                        }).filter(Boolean);

                        console.log(`[WEBHOOK] Parsed items:`, JSON.stringify(items));

                        // Create orders for each cart item (async fulfillment via consumer)
                        try {
                            for (const item of items as any[]) {
                                const productType = `${item.station}-${item.fuelType}-${item.liters}L`;
                                const idempotencyKey = `${paymentIntent.id}-${productType}`;

                                // Check for duplicate (idempotency)
                                const existing = await ordersRepository.getOrderByIdempotencyKey(idempotencyKey);
                                if (existing) {
                                    console.log(`[WEBHOOK] Order already exists for ${idempotencyKey}, skipping`);
                                    continue;
                                }

                                console.log(`[WEBHOOK] Creating order: ${item.quantity}x ${productType} for user ${userId}`);

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

                                console.log(`[WEBHOOK] Created order ${order.id} (async fulfillment pending)`);
                            }
                            console.log(`[WEBHOOK] Order creation complete for user ${userId}`);
                        } catch (error: any) {
                            console.error('[WEBHOOK] Order creation failed:', error.message);
                        }
                    } else {
                        console.log('[WEBHOOK] Skipping order creation - missing userId or cart items in metadata');
                    }
                    break;
                }

                case 'payment_intent.payment_failed': {
                    const paymentIntent = event.data.object as Stripe.PaymentIntent;
                    console.log('PaymentIntent failed:', paymentIntent.id);
                    // Handle failed payment
                    break;
                }

                default:
                    console.log(`Unhandled event type: ${event.type}`);
            }

            // Return 200 to acknowledge receipt
            res.json({ received: true });
        } catch (error: any) {
            console.error('Webhook error:', error.message);
            res.status(400).send(`Webhook Error: ${error.message}`);
        }
    }
);

export default router;
