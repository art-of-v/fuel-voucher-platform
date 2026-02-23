/**
 * Auth Controller
 *
 * Handles phone OTP authentication endpoints.
 */

import { Router, Request, Response, NextFunction } from "express";
import { AuthService } from "../../../application/services/auth.service";
import { AppError } from "../../../shared/errors/app-error";
import { createRateLimiter } from "../middleware/rate-limit.middleware";
import { config } from "../../../config";

const sendCodeLimiter = createRateLimiter({
    windowMs: 60_000,        // 1 minute
    maxRequests: 3,
    keyGenerator: (req) => `send:${req.ip}`,
});

const verifyCodeLimiter = createRateLimiter({
    windowMs: 5 * 60_000,    // 5 minutes
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
        this.router.post("/phone/send-code", sendCodeLimiter, this.sendCode.bind(this));
        this.router.post("/phone/verify", verifyCodeLimiter, this.verifyCode.bind(this));
        this.router.get("/phone/user", this.getCurrentUser.bind(this));
        this.router.post("/phone/logout", this.logout.bind(this));

        // Dev-only quick login — guarded here AND at the route registration level
        if (config.app.isDev) {
            this.router.post("/dev-login", this.devLogin.bind(this));
        }
    }

    private async sendCode(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { phone } = req.body;
            if (!phone || typeof phone !== "string") {
                throw AppError.badRequest("Phone number is required");
            }
            await this.authService.sendVerificationCode(phone);
            res.json({ success: true, message: "Verification code sent" });
        } catch (error) {
            next(error);
        }
    }

    private async verifyCode(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { phone, code } = req.body;
            if (!phone || !code) {
                throw AppError.badRequest("Phone and code are required");
            }

            const user = await this.authService.verifyPhone(phone, code);

            req.session.regenerate((regenErr) => {
                if (regenErr) {
                    return next(AppError.internal("Session regeneration failed"));
                }

                (req.session as any).userId = user.id;
                (req.session as any).phoneAuth = true;

                req.session.save((saveErr) => {
                    if (saveErr) {
                        return next(AppError.internal("Session save failed"));
                    }
                    res.json({ success: true, user });
                });
            });
        } catch (error) {
            next(error);
        }
    }

    private async getCurrentUser(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const session = req.session as any;
            let userId: string | undefined = session?.userId;
            const isPhoneAuth: boolean = session?.phoneAuth === true;

            if ((!userId || !isPhoneAuth) && config.app.isDev) {
                userId = config.app.devUserId;
            } else if (!userId || !isPhoneAuth) {
                throw AppError.unauthorized("Not authenticated");
            }

            const user = await this.authService.getUserById(userId);
            if (!user) {
                throw AppError.notFound("User");
            }

            res.json(user);
        } catch (error) {
            next(error);
        }
    }

    private async logout(req: Request, res: Response, next: NextFunction): Promise<void> {
        req.session.destroy((err) => {
            if (err) {
                return next(AppError.internal("Failed to logout"));
            }
            res.json({ success: true });
        });
    }

    private async devLogin(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const user = await this.authService.getOrCreateDevUser();

            req.session.regenerate((err) => {
                if (err) {
                    return next(AppError.internal("Session regeneration failed"));
                }
                // Must set phoneAuth so requireAuth middleware accepts this session
                (req.session as any).userId = user.id;
                (req.session as any).phoneAuth = true;

                res.json({ success: true, user });
            });
        } catch (error) {
            next(error);
        }
    }
}
