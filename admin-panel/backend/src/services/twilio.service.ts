import twilio from 'twilio';

const accountSid = process.env.TWILIO_ACCOUNT_SID;
const authToken = process.env.TWILIO_AUTH_TOKEN;
const twilioPhoneNumber = process.env.TWILIO_PHONE_NUMBER;

if (!accountSid || !authToken || !twilioPhoneNumber) {
    console.warn('⚠️  Twilio credentials not configured. SMS functionality will be disabled.');
}

const client = accountSid && authToken ? twilio(accountSid, authToken) : null;

export class TwilioService {
    /**
     * Send SMS verification code
     */
    async sendVerificationCode(phoneNumber: string, code: string): Promise<boolean> {
        if (!client) {
            console.error('Twilio client not initialized');
            return false;
        }

        try {
            const message = await client.messages.create({
                body: `Your Fuel Flow verification code is: ${code}. Valid for 10 minutes.`,
                from: twilioPhoneNumber,
                to: phoneNumber,
            });

            console.log(`✅ SMS sent successfully to ${phoneNumber}. SID: ${message.sid}`);
            return true;
        } catch (error: any) {
            console.error('❌ Twilio SMS error:', error.message);

            // Handle common errors
            if (error.code === 21211) {
                console.error('Invalid phone number format. Use E.164 format (e.g., +380XXXXXXXXX)');
            } else if (error.code === 21608) {
                console.error('Phone number is not verified. Add it to verified numbers in Twilio console.');
            }

            return false;
        }
    }

    /**
     * Generate a 6-digit verification code
     */
    generateVerificationCode(): string {
        return Math.floor(100000 + Math.random() * 900000).toString();
    }
}

export const twilioService = new TwilioService();
