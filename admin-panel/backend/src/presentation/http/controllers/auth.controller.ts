import { Router, Request, Response, NextFunction } from "express";
import { AuthService } from "../../../application/services/auth.service";
import { AppError } from "../../../shared/errors/app-error";
import { createRateLimiter } from "../middleware/rate-limit.middleware";
import { verifyApiSignature } from "../middleware/signature.middleware";
// import { config } from "../../../config";

const sendCodeLimiter = createRateLimiter({
    windowMs: 60_000,
    maxRequests: 3,
    keyGenerator: (req) => `send:${req.ip}`,
});

const verifyCodeLimiter = createRateLimiter({
    windowMs: 5 * 60_000,
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

        this.router.post("/device/register", this.registerDevice.bind(this));
        this.router.post("/device/challenge", this.requestChallenge.bind(this));
        this.router.post("/device/verify", this.verifyDevice.bind(this));
        this.router.post("/device/refresh", this.refresh.bind(this));
        this.router.post("/device/logout", verifyApiSignature, this.logout.bind(this));

        this.router.get("/user/me", verifyApiSignature, this.getCurrentUser.bind(this));
    }

    private async sendCode(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { phone } = req.body;
            await this.authService.sendVerificationCode(phone);
            res.json({ success: true, message: "Verification code sent" });
        } catch (error) {
            next(error);
        }
    }

    private async verifyCode(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { phone, code } = req.body;
            const user = await this.authService.verifyPhone(phone, code);
            res.json({ success: true, userId: user.id });
        } catch (error) {
            next(error);
        }
    }

    private async registerDevice(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const device = await this.authService.registerDevice(req.body);
            res.json({ success: true, deviceId: device.deviceId });
        } catch (error) {
            next(error);
        }
    }

    private async requestChallenge(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { deviceId } = req.body;
            const challenge = await this.authService.generateChallenge(deviceId);
            res.json({ success: true, challenge });
        } catch (error) {
            next(error);
        }
    }

    private async verifyDevice(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { deviceId, challenge, signature } = req.body;
            const tokens = await this.authService.verifyDeviceChallenge(deviceId, challenge, signature);
            res.json({ success: true, ...tokens });
        } catch (error) {
            next(error);
        }
    }

    private async refresh(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { refreshToken } = req.body;
            const accessToken = await this.authService.refreshSession(refreshToken);
            res.json({ success: true, access_token: accessToken });
        } catch (error) {
            next(error);
        }
    }

    private async logout(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const { refreshToken } = req.body;
            const accessToken = req.headers.authorization?.replace('Bearer ', '');
            await this.authService.logout(refreshToken, accessToken);
            res.json({ success: true, message: "Logged out" });
        } catch (error) {
            next(error);
        }
    }

    private async getCurrentUser(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).userId;
            if (!userId) throw AppError.unauthorized("Not authenticated");

            const user = await this.authService.getUserById(userId);
            if (!user) throw AppError.notFound("User");

            res.json(user);
        } catch (error) {
            next(error);
        }
    }
}
