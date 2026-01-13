import { Router, Request, Response } from 'express';
import { paymentService } from '../services/payment.service';
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
                    // Handle successful payment intent
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
