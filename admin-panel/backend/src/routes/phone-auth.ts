import { Router, Request, Response } from 'express';
import { twilioService } from '../services/twilio.service';

const router = Router();

// In-memory store for verification codes (in production, use Redis)
interface VerificationCode {
    code: string;
    phoneNumber: string;
    expiresAt: Date;
    attempts: number;
}

const verificationCodes = new Map<string, VerificationCode>();

// Clean up expired codes every 5 minutes
setInterval(() => {
    const now = new Date();
    for (const [phone, data] of verificationCodes.entries()) {
        if (data.expiresAt < now) {
            verificationCodes.delete(phone);
        }
    }
}, 5 * 60 * 1000);

/**
 * POST /api/auth/phone/send-code
 * Send verification code to phone number
 */
router.post('/send-code', async (req: Request, res: Response) => {
    try {
        const { phoneNumber } = req.body;

        if (!phoneNumber) {
            return res.status(400).json({ error: 'Phone number is required' });
        }

        // Validate phone number format (E.164)
        const phoneRegex = /^\+[1-9]\d{1,14}$/;
        if (!phoneRegex.test(phoneNumber)) {
            return res.status(400).json({
                error: 'Invalid phone number format. Use E.164 format (e.g., +380XXXXXXXXX)'
            });
        }

        // Rate limiting: Check if code was sent recently
        const existing = verificationCodes.get(phoneNumber);
        if (existing && existing.expiresAt > new Date()) {
            const timeLeft = Math.ceil((existing.expiresAt.getTime() - Date.now()) / 1000 / 60);
            return res.status(429).json({
                error: `Code already sent. Please wait ${timeLeft} minutes before requesting a new code.`
            });
        }

        // Send SMS
        let sent = false;
        let code = "";

        // TEST MODE BYPASS
        if (phoneNumber === '+380000000000') {
            code = "000000";
            sent = true;
            console.log('🧪 TEST MODE: Bypass SMS for +380000000000. Code: 000000');
        } else {
            // Generate verification code
            code = twilioService.generateVerificationCode();
            // Send SMS
            sent = await twilioService.sendVerificationCode(phoneNumber, code);
        }

        if (!sent) {
            return res.status(500).json({
                error: 'Failed to send SMS. Please check if your phone number is verified in Twilio console.'
            });
        }

        // Store verification code
        verificationCodes.set(phoneNumber, {
            code,
            phoneNumber,
            expiresAt: new Date(Date.now() + 10 * 60 * 1000), // 10 minutes
            attempts: 0,
        });

        res.json({
            success: true,
            message: 'Verification code sent successfully',
            expiresIn: 600 // seconds
        });
    } catch (error: any) {
        console.error('Send code error:', error);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /api/auth/phone/verify-code
 * Verify the code and authenticate user
 */
router.post('/verify-code', async (req: Request, res: Response) => {
    try {
        const { phoneNumber, code } = req.body;

        if (!phoneNumber || !code) {
            return res.status(400).json({ error: 'Phone number and code are required' });
        }

        const verification = verificationCodes.get(phoneNumber);

        if (!verification) {
            return res.status(400).json({ error: 'No verification code found. Please request a new code.' });
        }

        // Check if code expired
        if (verification.expiresAt < new Date()) {
            verificationCodes.delete(phoneNumber);
            return res.status(400).json({ error: 'Verification code expired. Please request a new code.' });
        }

        // Check attempts
        if (verification.attempts >= 3) {
            verificationCodes.delete(phoneNumber);
            return res.status(429).json({ error: 'Too many failed attempts. Please request a new code.' });
        }

        // Verify code
        if (verification.code !== code) {
            verification.attempts++;
            return res.status(400).json({
                error: 'Invalid verification code',
                attemptsLeft: 3 - verification.attempts
            });
        }

        // Code is valid - clean up and create session
        verificationCodes.delete(phoneNumber);

        // TODO: Create or get user from database
        // For now, we'll create a simple user object
        const user = {
            id: `user_${phoneNumber.replace(/\+/g, '')}`,
            phoneNumber,
            createdAt: new Date(),
        };

        // Set session
        if (req.session) {
            req.session.userId = user.id;
            req.session.phoneNumber = phoneNumber;
        }

        res.json({
            success: true,
            message: 'Phone number verified successfully',
            user: {
                id: user.id,
                phoneNumber: user.phoneNumber,
            }
        });
    } catch (error: any) {
        console.error('Verify code error:', error);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /api/auth/phone/logout
 * Logout user
 */
router.post('/logout', (req: Request, res: Response) => {
    if (req.session) {
        req.session.destroy((err) => {
            if (err) {
                return res.status(500).json({ error: 'Failed to logout' });
            }
            res.json({ success: true, message: 'Logged out successfully' });
        });
    } else {
        res.json({ success: true, message: 'Already logged out' });
    }
});

/**
 * GET /api/auth/phone/me
 * Get current user
 */
router.get('/me', (req: Request, res: Response) => {
    if (req.session?.userId) {
        res.json({
            user: {
                id: req.session.userId,
                phoneNumber: req.session.phoneNumber,
            }
        });
    } else {
        res.status(401).json({ error: 'Not authenticated' });
    }
});

export default router;
