import { stripe, STRIPE_CONFIG } from '../config/stripe';
import Stripe from 'stripe';

export class PaymentService {
    /**
     * Create a Stripe Checkout Session for fuel package purchase
     */
    async createCheckoutSession(params: {
        userId: string;
        packageId: string;
        packageName: string;
        amount: number; // in UAH
        quantity: number;
        metadata?: Record<string, string>;
    }): Promise<Stripe.Checkout.Session> {
        const { userId, packageId, packageName, amount, quantity, metadata } = params;

        const session = await stripe.checkout.sessions.create({
            payment_method_types: ['card'],
            line_items: [
                {
                    price_data: {
                        currency: STRIPE_CONFIG.currency,
                        product_data: {
                            name: packageName,
                            description: `Fuel package: ${packageName}`,
                        },
                        unit_amount: Math.round(amount * 100), // Convert to cents
                    },
                    quantity,
                },
            ],
            mode: 'payment',
            success_url: STRIPE_CONFIG.successUrl + '?session_id={CHECKOUT_SESSION_ID}',
            cancel_url: STRIPE_CONFIG.cancelUrl,
            client_reference_id: userId,
            metadata: {
                userId,
                packageId,
                ...metadata,
            },
        });

        return session;
    }

    /**
     * Create a Payment Intent (for custom payment flows)
     */
    async createPaymentIntent(params: {
        amount: number; // in UAH
        userId: string;
        metadata?: Record<string, string>;
    }): Promise<Stripe.PaymentIntent> {
        const { amount, userId, metadata } = params;

        const paymentIntent = await stripe.paymentIntents.create({
            amount: Math.round(amount * 100), // Convert to cents
            currency: STRIPE_CONFIG.currency,
            metadata: {
                userId,
                ...metadata,
            },
        });

        return paymentIntent;
    }

    /**
     * Retrieve a Checkout Session
     */
    async getCheckoutSession(sessionId: string): Promise<Stripe.Checkout.Session> {
        return await stripe.checkout.sessions.retrieve(sessionId);
    }

    /**
     * Verify webhook signature
     */
    verifyWebhookSignature(payload: string | Buffer, signature: string): Stripe.Event {
        return stripe.webhooks.constructEvent(
            payload,
            signature,
            STRIPE_CONFIG.webhookSecret
        );
    }

    /**
     * Handle successful payment
     */
    async handleSuccessfulPayment(session: Stripe.Checkout.Session) {
        const userId = session.metadata?.userId;
        const packageId = session.metadata?.packageId;

        // TODO: Implement your business logic here:
        // 1. Create purchase record in database
        // 2. Generate QR codes for fuel vouchers
        // 3. Send confirmation email
        // 4. Update user's fuel balance

        console.log('Payment successful:', {
            sessionId: session.id,
            userId,
            packageId,
            amountTotal: session.amount_total,
        });

        return {
            success: true,
            userId,
            packageId,
        };
    }
}

export const paymentService = new PaymentService();
