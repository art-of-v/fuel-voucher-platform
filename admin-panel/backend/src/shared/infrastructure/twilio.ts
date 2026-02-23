/**
 * SMS Service
 *
 * Provider-agnostic SMS sending with Twilio primary and dev bypass.
 * SMS_PROVIDER env var: 'twilio' | 'dev' (default: 'twilio')
 */

import { logger } from "../../infrastructure/logging/logger";

const log = logger.child({ component: "SMS" });

const SMS_PROVIDER = process.env.SMS_PROVIDER || "twilio";

// ─── Twilio ────────────────────────────────────────────────────────────────

async function sendSMSWithTwilio(to: string, body: string): Promise<boolean> {
  const sid = process.env.TWILIO_ACCOUNT_SID;
  const token = process.env.TWILIO_AUTH_TOKEN;
  const from = process.env.TWILIO_PHONE_NUMBER;

  if (!sid || !token || !from) {
    log.warn("Twilio credentials not configured — SMS not sent");
    return false;
  }

  try {
    const twilio = await import("twilio");
    const client = twilio.default(sid, token);
    await client.messages.create({ body, from, to });
    log.info({ to: to.slice(0, 4) + "****" }, "SMS sent via Twilio");
    return true;
  } catch (error) {
    log.error({ err: error }, "Twilio SMS failed");
    return false;
  }
}

// ─── Main ──────────────────────────────────────────────────────────────────

export async function sendSMS(to: string, body: string): Promise<boolean> {
  // Development bypass — log to console, no external call
  if (SMS_PROVIDER === "dev" || process.env.NODE_ENV === "development") {
    log.debug({ to: to.slice(0, 4) + "****" }, "[DEV] SMS suppressed");
    return true;
  }

  return sendSMSWithTwilio(to, body);
}

/**
 * Send an OTP verification code via SMS.
 */
export async function sendVerificationCode(phone: string, code: string): Promise<boolean> {
  const message = `Your FuelFlow verification code is: ${code}. Valid for 5 minutes.`;
  return sendSMS(phone, message);
}
