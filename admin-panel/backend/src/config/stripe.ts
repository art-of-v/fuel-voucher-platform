import Stripe from 'stripe';
import dotenv from 'dotenv';

dotenv.config();

if (!process.env.STRIPE_SECRET_KEY) {
    throw new Error('STRIPE_SECRET_KEY is not set in environment variables');
}

// Initialize Stripe with your secret key
export const stripe = new Stripe(process.env.STRIPE_SECRET_KEY, {
    apiVersion: '2024-12-18.acacia',
    typescript: true,
});

export const STRIPE_CONFIG = {
    publishableKey: process.env.STRIPE_PUBLISHABLE_KEY || '',
    webhookSecret: process.env.STRIPE_WEBHOOK_SECRET || '',
    currency: 'uah', // Ukrainian Hryvnia
    successUrl: process.env.STRIPE_SUCCESS_URL || 'http://localhost:5000/payment-success',
    cancelUrl: process.env.STRIPE_CANCEL_URL || 'http://localhost:5000/payment-cancel',
};
