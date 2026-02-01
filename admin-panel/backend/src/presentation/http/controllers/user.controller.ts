/**
 * User Controller
 * 
 * Handles user-related HTTP endpoints.
 */

import { Router, Request, Response, NextFunction } from 'express';
import { UserService } from '../../../application/services/user.service';
import { requireAuth } from '../middleware/auth.middleware';

export class UserController {
    public readonly router: Router;

    constructor(private readonly userService: UserService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        // Update user profile
        this.router.post('/update', requireAuth, this.updateProfile.bind(this));

        // Create referral code
        this.router.post('/create', requireAuth, this.createReferralCode.bind(this));

        // Redeem referral code
        this.router.post('/redeem', requireAuth, this.redeemReferralCode.bind(this));
    }

    private async updateProfile(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).authUserId!;
            const data = req.body;

            const user = await this.userService.updateUser(userId, data);
            res.json(user);
        } catch (error) {
            next(error);
        }
    }

    private async createReferralCode(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).authUserId!;
            const { code } = req.body;

            const user = await this.userService.createReferralCode(userId, code);
            res.json(user);
        } catch (error) {
            next(error);
        }
    }

    private async redeemReferralCode(req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const userId = (req as any).authUserId!;
            const { code } = req.body;

            await this.userService.redeemReferralCode(userId, code);
            res.json({ success: true, message: 'Referral code redeemed' });
        } catch (error) {
            next(error);
        }
    }
}

/**
 * Admin User Controller
 * 
 * Handles admin user management endpoints.
 */
export class AdminUserController {
    public readonly router: Router;

    constructor(private readonly userService: UserService) {
        this.router = Router();
        this.registerRoutes();
    }

    private registerRoutes(): void {
        // Get all users
        this.router.get('/', this.getAllUsers.bind(this));
    }

    private async getAllUsers(_req: Request, res: Response, next: NextFunction): Promise<void> {
        try {
            const users = await this.userService.getAllUsers();
            res.json(users);
        } catch (error) {
            next(error);
        }
    }
}
