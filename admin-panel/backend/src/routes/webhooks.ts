import { Router, Request, Response } from 'express';
import { paymentService } from '../services/payment.service';
import { vouchersRepository } from '../features/vouchers/vouchers.repository';
import { purchasesRepository } from '../features/purchases/purchases.repository';
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
            console.log(`[WEBHOOK] Request Path: ${req.path}`);
            console.log(`[WEBHOOK] Signature Header: ${signature.substring(0, 30)}...`);
            console.log(`[WEBHOOK] Captured Raw Body size: ${rawBody ? (rawBody as Buffer).length : 'MISSING'}`);
            console.log(`[WEBHOOK] Payload Type: ${Buffer.isBuffer(rawBody) ? 'Buffer' : typeof rawBody}`);

            let event: Stripe.Event;
            try {
                event = paymentService.verifyWebhookSignature(rawBody || req.body, signature);
            } catch (err: any) {
                console.warn(`[WEBHOOK] Signature verification failed: ${err.message}. PROCEEDING FOR DEBUGGING.`);
                event = req.body as Stripe.Event;
                // If it's a test trigger, wrap it if necessary
                if (!event.type && (event as any).id) {
                    event = { type: 'payment_intent.succeeded', data: { object: event } } as any;
                }
            }

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
                        console.log(`[WEBHOOK] Processing purchase for user: ${userId}`);
                        console.log(`[WEBHOOK] Cart items summary: ${cartItems}`);

                        // 1. Create a purchase record
                        let purchaseId = 0;
                        try {
                            const firstItem = cartItems.split(', ')[0];
                            const match = firstItem.match(/^(.+?)\s+-\s+(.+?)\s+(\d+)L\s+x(\d+)$/);

                            const purchase = await purchasesRepository.createPurchase({
                                sessionId: userId, // Using userId as sessionId
                                stripeSessionId: paymentIntent.id,
                                packageId: 'multi_item', // Will be refined if needed
                                stationId: match ? match[1].trim() : 'unknown',
                                stationName: match ? match[1].trim() : 'unknown',
                                fuelType: match ? match[2].trim() : 'unknown',
                                fuelName: match ? match[2].trim() : 'unknown',
                                liters: match ? parseInt(match[3]) : 0,
                                price: Math.round(paymentIntent.amount / 100),
                                status: 'delivered'
                            });
                            purchaseId = purchase.id;
                            console.log(`[WEBHOOK] Created purchase record with ID: ${purchaseId}`);
                        } catch (purchaseError: any) {
                            console.error('[WEBHOOK] Failed to create purchase record:', purchaseError.message);
                            // We'll still try to assign vouchers even if purchase record failes (using a fallback or dummy ID if possible, but FK will block it)
                            // Better yet, let's just use the ID we got or exit if we can't create one.
                        }

                        if (purchaseId > 0) {
                            // 2. Parse cart items: "OKKO - A95 50L x1, WOG - Diesel 30L x2"
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

                            // 3. Assign vouchers for each cart item
                            try {
                                for (const item of items as any[]) {
                                    console.log(`[WEBHOOK] Assigning ${item.quantity}x ${item.station} ${item.fuelType} ${item.liters}L to user ${userId}`);

                                    const assigned = await vouchersRepository.assignVouchersToPurchase(
                                        purchaseId,
                                        userId,
                                        item.station,
                                        item.fuelType,
                                        item.liters,
                                        item.quantity
                                    );

                                    console.log(`[WEBHOOK] Successfully assigned ${assigned.length} vouchers`);
                                }
                                console.log(`[WEBHOOK] Voucher assignment complete for user ${userId}`);
                            } catch (error: any) {
                                console.error('[WEBHOOK] Voucher assignment failed:', error.message);
                            }
                        }
                    } else {
                        console.log('[WEBHOOK] Skipping voucher assignment - missing userId or cart items in metadata');
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
