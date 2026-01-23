import twilio from 'twilio';

const accountSid = process.env.TWILIO_ACCOUNT_SID;
const authToken = process.env.TWILIO_AUTH_TOKEN;
const phoneNumber = process.env.TWILIO_PHONE_NUMBER;

let twilioClient: twilio.Twilio | null = null;

function getClient(): twilio.Twilio {
  if (!twilioClient) {
    if (!accountSid || !authToken) {
      throw new Error('Twilio credentials not configured');
    }
    twilioClient = twilio(accountSid, authToken);
  }
  return twilioClient;
}

export async function sendSMS(to: string, body: string): Promise<boolean> {
  if (!process.env.TWILIO_ACCOUNT_SID || process.env.NODE_ENV === 'development') {
    console.log(`[DEV/Bypass Twilio] Sending SMS to ${to}: ${body}`);
    return true;
  }

  try {
    const client = getClient();
    if (!phoneNumber) {
      throw new Error('Twilio phone number not configured');
    }

    await client.messages.create({
      body,
      from: phoneNumber,
      to,
    });

    return true;
  } catch (error) {
    console.error('Failed to send SMS:', error);
    return false;
  }
}

export function generateVerificationCode(): string {
  return "000000";
}

export async function sendVerificationCode(phone: string, code: string): Promise<boolean> {
  const message = `Your Lemberg Fuel verification code is: ${code}. Valid for 5 minutes.`;
  return sendSMS(phone, message);
}
