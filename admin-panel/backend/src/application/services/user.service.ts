/**
 * User Service
 * 
 * Handles user management and referral logic.
 */

import { IUserRepository, User, UpdateUserData } from '../../domain/repositories/user.repository';
import { AppError } from '../../shared/errors/app-error';
import { logger } from '../../infrastructure/logging/logger';

/**
 * Notification repository interface
 */
export interface INotificationRepository {
    createNotification(data: { userId: string; title: string; message: string }): Promise<void>;
}

/**
 * Referral bonus amounts (in UAH)
 */
const REFERRER_BONUS = 50;
const REFEREE_BONUS = 20;

export class UserService {
    private readonly log = logger.child({ component: 'UserService' });

    constructor(
        private readonly userRepository: IUserRepository,
        private readonly notificationRepository: INotificationRepository,
    ) { }

    /**
     * Get all users (admin)
     */
    async getAllUsers(): Promise<User[]> {
        return this.userRepository.findAll();
    }

    /**
     * Get user by ID
     */
    async getUserById(userId: string): Promise<User | null> {
        return this.userRepository.findById(userId);
    }

    /**
     * Update user profile
     */
    async updateUser(userId: string, data: UpdateUserData): Promise<User> {
        const user = await this.userRepository.findById(userId);
        if (!user) {
            throw AppError.notFound('User');
        }

        return this.userRepository.update(userId, data);
    }

    /**
     * Create referral code
     */
    async createReferralCode(userId: string, code: string): Promise<User> {
        // Check if code is already taken
        const existing = await this.userRepository.findByReferralCode(code);
        if (existing) {
            throw AppError.conflict('Referral code already taken');
        }

        return this.userRepository.update(userId, { referralCode: code });
    }

    /**
     * Redeem referral code
     */
    async redeemReferralCode(userId: string, code: string): Promise<void> {
        // 1. Validate code
        const referrer = await this.userRepository.findByReferralCode(code);
        if (!referrer) {
            throw AppError.notFound('Referral code');
        }

        // 2. Validate user isn't redeeming their own code
        if (referrer.id === userId) {
            throw AppError.badRequest('Cannot redeem your own code');
        }

        // 3. Check if user was already referred
        const currentUser = await this.userRepository.findById(userId);
        if (!currentUser) {
            throw AppError.notFound('User');
        }
        if (currentUser.referredBy) {
            throw AppError.badRequest('You have already redeemed a referral code');
        }

        // 4. Apply referral
        await this.userRepository.update(userId, { referredBy: referrer.id });

        // 5. Credit bonus to Referrer
        const referrerBonus = (referrer.bonusBalance || 0) + REFERRER_BONUS;
        await this.userRepository.update(referrer.id, { bonusBalance: referrerBonus });

        // 6. Credit bonus to Referee
        const userBonus = (currentUser.bonusBalance || 0) + REFEREE_BONUS;
        await this.userRepository.update(userId, { bonusBalance: userBonus });

        // 7. Notify Referrer
        await this.notificationRepository.createNotification({
            userId: referrer.id,
            title: 'New Referral Reward!',
            message: `User ${currentUser.phone || 'someone'} used your code! You got ${REFERRER_BONUS} UAH bonus.`,
        });

        this.log.info({ userId, referrerId: referrer.id }, 'Referral code redeemed');
    }
}
