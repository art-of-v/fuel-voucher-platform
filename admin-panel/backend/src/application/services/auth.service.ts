/**
 * Auth Service
 * 
 * Handles authentication and verification logic.
 */

import { IUserRepository, User } from '../../domain/repositories/user.repository';
import { AppError } from '../../shared/errors/app-error';
import { logger } from '../../infrastructure/logging/logger';

/**
 * Phone verification record
 */
export interface PhoneVerification {
    id: number;
    phone: string;
    code: string;
    expiresAt: Date;
    verified: boolean;
}

/**
 * Verification repository interface
 */
export interface IVerificationRepository {
    createPhoneVerification(phone: string, code: string): Promise<PhoneVerification>;
    getLatestPhoneVerification(phone: string): Promise<PhoneVerification | null>;
    markPhoneVerified(id: number): Promise<void>;
}

/**
 * SMS sender interface
 */
export interface ISMSSender {
    sendVerificationCode(phone: string, code: string): Promise<boolean>;
}

export class AuthService {
    private readonly log = logger.child({ component: 'AuthService' });

    constructor(
        private readonly userRepository: IUserRepository,
        private readonly verificationRepository: IVerificationRepository,
        private readonly smsSender: ISMSSender,
    ) { }

    /**
     * Generate a 6-digit verification code
     */
    generateVerificationCode(): string {
        // ALWAYS return 000000 for emulation/testing as requested
        return "000000";
    }

    /**
     * Normalize phone number to E.164 format (specifically for Ukraine)
     */
    normalizePhone(phone: string): string {
        // Strip all non-digits except +
        let sanitized = phone.replace(/[^\d+]/g, '');

        // Handle common Ukraine formats
        if (sanitized.startsWith('0') && sanitized.length === 10) {
            sanitized = '+38' + sanitized;
        } else if (sanitized.startsWith('380') && sanitized.length === 12) {
            sanitized = '+' + sanitized;
        } else if (!sanitized.startsWith('+')) {
            sanitized = '+' + sanitized;
        }

        return sanitized;
    }

    /**
     * Validate phone number format
     */
    validatePhone(phone: string): boolean {
        const sanitized = phone.replace(/[\s-]/g, '');
        const phoneRegex = /^\+?[1-9]\d{6,14}$/;
        return phoneRegex.test(sanitized);
    }

    /**
     * Validate verification code format
     */
    validateCode(code: string): boolean {
        return typeof code === 'string' && code.length === 6 && /^\d+$/.test(code);
    }

    /**
     * Send verification code to phone
     */
    async sendVerificationCode(phone: string): Promise<void> {
        if (!this.validatePhone(phone)) {
            throw AppError.badRequest('Invalid phone number format');
        }

        const normalizedPhone = this.normalizePhone(phone);
        const code = this.generateVerificationCode();

        this.log.info({ phone: normalizedPhone }, 'Sending verification code');

        // Log code in development
        if (process.env.NODE_ENV !== 'production') {
            this.log.debug({ phone: normalizedPhone, code }, '[DEV] Verification code');
        }

        const isSent = await this.smsSender.sendVerificationCode(normalizedPhone, code);

        if (!isSent) {
            throw AppError.internal('Failed to send SMS');
        }

        await this.verificationRepository.createPhoneVerification(normalizedPhone, code);
    }

    /**
     * Verify phone with code
     */
    async verifyPhone(phone: string, code: string): Promise<User> {
        if (!this.validateCode(code)) {
            throw AppError.badRequest('Invalid code format');
        }

        const normalizedPhone = this.normalizePhone(phone);

        const verificationRecord = await this.verificationRepository.getLatestPhoneVerification(normalizedPhone);

        if (!verificationRecord) {
            throw AppError.badRequest('No verification pending or code expired');
        }

        if (verificationRecord.code !== code) {
            throw AppError.badRequest('Invalid verification code');
        }

        await this.verificationRepository.markPhoneVerified(verificationRecord.id);

        // Find or create user
        let user = await this.userRepository.findByPhone(normalizedPhone);
        if (!user) {
            user = await this.userRepository.createWithPhone(normalizedPhone);
            this.log.info({ userId: user.id, phone: normalizedPhone }, 'New user created via phone verification');
        }

        this.log.info({ userId: user.id }, 'User authenticated via phone');

        return user;
    }

    /**
     * Get user by ID
     */
    async getUserById(userId: string): Promise<User | null> {
        return this.userRepository.findById(userId);
    }

    /**
     * Get user by email (for dev login)
     */
    async getUserByEmail(email: string): Promise<User | null> {
        return this.userRepository.findByEmail(email);
    }

    /**
     * Create or get dev user
     */
    async getOrCreateDevUser(): Promise<User> {
        const email = 'dev@example.com';
        let user = await this.userRepository.findByEmail(email);

        if (!user) {
            user = await this.userRepository.create({
                email,
                firstName: 'Dev',
                lastName: 'Tester',
            });
            this.log.info({ userId: user.id }, 'Dev user created');
        }

        return user;
    }
}
