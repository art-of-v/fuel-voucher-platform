/**
 * Auth Controller
 * 
 * Handles authentication-related HTTP endpoints.
 */

import { Router, Request, Response, NextFunction } from 'express';
import { AuthService } from '../../../application/services/auth.service';
import { AppError } from '../../../shared/errors/app-error';
import { createRateLimiter } from '../middleware/rate-limit.middleware';
import { config } from '../../../config';

// Rate limiters for OTP endpoints
const sendCodeLimiter = createRateLimiter({
    windowMs: 60 * 1000, // 1 minute
    maxRequests: 3,
    keyGenerator: (req) => `send:${req.ip}`,
});

const verifyCodeLimiter = createRateLimiter({
    windowMs: 5 * 60 * 1000, // 5 minutes
    maxRequests: 5,
    keyGenerator: (req) => `verify:${req.ip}`,
});

export class AuthController {
    public readonly router: Router;

    constructor(private readonly authService: AuthService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        // Send verification code
        this.router.post('/phone/send-code', sendCodeLimiter, this.sendCode.bind(this));

        // Verify code
        this.router.post('/phone/verify', verifyCodeLimiter, this.verifyCode.bind(this));

        // Get current user
        this.router.get('/phone/user', this.getCurrentUser.bind(this));

        // Logout
        this.router.post('/phone/logout', this.logout.bind(this));

        // Dev login (non-production only)
        if (process.env.NODE_ENV !== 'production') {
            this.router.post('/dev-login', this.devLogin.bind(this));
        }
    }

    private async sendCode(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { phone } = req.body;

            if (!phone || typeof phone !== 'string') {
                throw AppError.badRequest('Phone number is required');
            }

            await this.authService.sendVerificationCode(phone);

            res.json({ success: true, message: 'Verification code sent' });
        } catch (error) {
            next(error);
        }
    }

    private async verifyCode(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { phone, code } = req.body;

            if (!phone || !code) {
                throw AppError.badRequest('Phone and code are required');
            }

            const user = await this.authService.verifyPhone(phone, code);

            // Regenerate session
            req.session.regenerate((err) => {
                if (err) {
                    console.error('Session regeneration error:', err);
                    return res.status(500).json({ error: 'Session error' });
                }

                (req.session as any).userId = user.id;
                (req.session as any).phoneAuth = true;

                res.json({ success: true, user });
            });
        } catch (error) {
            next(error);
        }
    }

    private async getCurrentUser(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            let userId = (req.session as any)?.userId;
            const isPhoneAuth = (req.session as any)?.phoneAuth;

            // DEV FALLBACK
            if ((!userId || !isPhoneAuth) && process.env.NODE_ENV !== 'production') {
                userId = config.app.devUserId;
            } else if (!userId || !isPhoneAuth) {
                throw AppError.unauthorized('Not authenticated');
            }

            const user = await this.authService.getUserById(userId);
            if (!user) {
                throw AppError.notFound('User');
            }

            res.json(user);
        } catch (error) {
            next(error);
        }
    }

    private async logout(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            req.session.destroy((err) => {
                if (err) {
                    return res.status(500).json({ error: 'Failed to logout' });
                }
                res.json({ success: true });
            });
        } catch (error) {
            next(error);
        }
    }

    private async devLogin(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const user = await this.authService.getOrCreateDevUser();

            req.session.regenerate((err) => {
                if (err) {
                    console.error('Session error:', err);
                    return res.status(500).json({ error: 'Session creation failed' });
                }
                (req.session as any).userId = user.id;

                res.json({ success: true, user });
            });
        } catch (error) {
            next(error);
        }
    }
}
