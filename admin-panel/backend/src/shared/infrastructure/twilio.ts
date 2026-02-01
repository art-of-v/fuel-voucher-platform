/**
 * SMS Service - Provider-agnostic SMS sending
 * Supports: Textbelt (free), Twilio (fallback)
 */

const SMS_PROVIDER = process.env.SMS_PROVIDER || 'textbelt';

// Textbelt configuration
const TEXTBELT_API_KEY = process.env.TEXTBELT_API_KEY || 'textbelt'; // 'textbelt' = free tier

// Twilio configuration (fallback)
const TWILIO_ACCOUNT_SID = process.env.TWILIO_ACCOUNT_SID;
const TWILIO_AUTH_TOKEN = process.env.TWILIO_AUTH_TOKEN;
const TWILIO_PHONE_NUMBER = process.env.TWILIO_PHONE_NUMBER;

/**
 * Send SMS using Textbelt API
 */
async function sendSMSWithTextbelt(to: string, body: string): Promise<boolean> {
  try {
    const response = await fetch('https://textbelt.com/text', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        phone: to,
        message: body,
        key: TEXTBELT_API_KEY,
      }),
    });

    const result = await response.json();

    if (result.success) {
      console.log(`[Textbelt] SMS sent successfully to ${to}`);
      return true;
    } else {
      console.error(`[Textbelt] Failed to send SMS: ${result.error}`);
      // Common errors: "Out of quota" (free tier limit), "Invalid phone number"
      return false;
    }
  } catch (error) {
    console.error('[Textbelt] Error sending SMS:', error);
    return false;
  }
}

/**
 * Send SMS using Twilio API (fallback)
 */
async function sendSMSWithTwilio(to: string, body: string): Promise<boolean> {
  try {
    // Lazy load twilio only if needed
    const twilio = await import('twilio');

    if (!TWILIO_ACCOUNT_SID || !TWILIO_AUTH_TOKEN || !TWILIO_PHONE_NUMBER) {
      throw new Error('Twilio credentials not configured');
    }

    const client = twilio.default(TWILIO_ACCOUNT_SID, TWILIO_AUTH_TOKEN);

    await client.messages.create({
      body,
      from: TWILIO_PHONE_NUMBER,
      to,
    });

    console.log(`[Twilio] SMS sent successfully to ${to}`);
    return true;
  } catch (error) {
    console.error('[Twilio] Failed to send SMS:', error);
    return false;
  }
}

/**
 * Main SMS sending function with provider selection
 */
export async function sendSMS(to: string, body: string): Promise<boolean> {
  // Development bypass - just log to console
  if (process.env.NODE_ENV === 'development') {
    console.log(`[SMS Dev Bypass] Sending to ${to}: ${body}`);
    return true;
  }

  // Select provider
  if (SMS_PROVIDER === 'textbelt') {
    return sendSMSWithTextbelt(to, body);
  } else if (SMS_PROVIDER === 'twilio') {
    return sendSMSWithTwilio(to, body);
  } else {
    console.error(`[SMS] Unknown provider: ${SMS_PROVIDER}`);
    return false;
  }
}

/**
 * Generate a verification code
 * Returns static 000000 for simplified auth
 */
export function generateVerificationCode(): string {
  return "000000";
}

/**
 * Send verification code via SMS
 */
export async function sendVerificationCode(phone: string, code: string): Promise<boolean> {
  const message = `Your Lemberg Fuel verification code is: ${code}. Valid for 5 minutes.`;
  return sendSMS(phone, message);
}
