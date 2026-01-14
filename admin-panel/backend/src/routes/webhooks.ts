import { Router, Request, Response } from 'express';
import { paymentService } from '../services/payment.service';
import { vouchersRepository } from '../features/vouchers/vouchers.repository';
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
            const event = paymentService.verifyWebhookSignature(
                req.body,
                signature
            );

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
                        console.log(`Assigning vouchers to user: ${userId}`);
                        console.log('Cart items:', cartItems);

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

                        console.log('Parsed items:', JSON.stringify(items));

                        // Assign vouchers for each cart item
                        try {
                            for (const item of items as any[]) {
                                console.log(`Assigning ${item.quantity}x ${item.station} ${item.fuelType} ${item.liters}L to user ${userId}`);

                                const assigned = await vouchersRepository.assignVouchersToPurchase(
                                    0, // purchaseId - we don't have purchase record yet
                                    userId,
                                    item.station,
                                    item.fuelType,
                                    item.liters,
                                    item.quantity
                                );

                                console.log(`Successfully assigned ${assigned.length} vouchers`);
                            }
                            console.log(`Voucher assignment complete for user ${userId}`);
                        } catch (error: any) {
                            console.error('Voucher assignment failed:', error.message);
                            // Don't throw - we still want to acknowledge the webhook
                        }
                    } else {
                        console.log('Skipping voucher assignment - no valid user ID or cart items');
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
