import Stripe from 'stripe';

async function getCredentials() {
  const secretKey = process.env.STRIPE_SECRET_KEY;
  const publishableKey = process.env.STRIPE_PUBLISHABLE_KEY;

  if (!secretKey || !publishableKey) {
    throw new Error('STRIPE_SECRET_KEY and STRIPE_PUBLISHABLE_KEY environment variables are required');
  }

  return { publishableKey, secretKey };
}

export async function getUncachableStripeClient() {
  try {
    const { secretKey } = await getCredentials();
    return new Stripe(secretKey);
  } catch (error) {
    console.warn("Stripe credentials not found, using Mock Stripe Client");
    return {
      checkout: {
        sessions: {
          create: async (params: any) => {
            console.log("Mock Stripe Checkout Session Created", params);
            return {
              url: `${params.success_url}&session_id=mock_stripe_session_${Date.now()}`
            };
          }
        }
      },
      webhooks: {
        constructEvent: (body: any, _sig: any, _secret: any) => {
          return { type: 'checkout.session.completed', data: { object: body } };
        }
      }
    } as any as Stripe;
  }
}

export async function getStripePublishableKey() {
  try {
    const { publishableKey } = await getCredentials();
    return publishableKey;
  } catch (e) {
    return "";
  }
}

export async function getStripeSecretKey() {
  try {
    const { secretKey } = await getCredentials();
    return secretKey;
  } catch (e) {
    return "";
  }
}

export async function getStripeSync() {
  // Dummy sync for non-Replit env
  return {
    findOrCreateManagedWebhook: async () => ({ webhook: {} }),
    syncBackfill: async () => { },
    processWebhook: async (_payload: Buffer, _signature: string, _uuid: string) => { },
    sync: async () => { },
  };
}

export function getWebhookSecret(): string {
  return process.env.STRIPE_WEBHOOK_SECRET || '';
}
