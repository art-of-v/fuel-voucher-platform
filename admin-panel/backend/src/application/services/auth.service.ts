/**
 * Auth Service
 *
 * Handles phone-based authentication and OTP verification.
 */

import { IUserRepository, User } from "../../domain/repositories/user.repository";
import { AppError } from "../../shared/errors/app-error";
import { logger } from "../../infrastructure/logging/logger";

// ─── Interfaces ──────────────────────────────────────────────────────────────

export interface PhoneVerification {
    id: number;
    phone: string;
    code: string;
    expiresAt: Date;
    verified: boolean;
}

export interface IVerificationRepository {
    createPhoneVerification(phone: string, code: string): Promise<PhoneVerification>;
    getLatestPhoneVerification(phone: string): Promise<PhoneVerification | null>;
    markPhoneVerified(id: number): Promise<void>;
}

export interface ISMSSender {
    sendVerificationCode(phone: string, code: string): Promise<boolean>;
}

// ─── Service ─────────────────────────────────────────────────────────────────

export class AuthService {
    private readonly log = logger.child({ component: "AuthService" });

    constructor(
        private readonly userRepository: IUserRepository,
        private readonly verificationRepository: IVerificationRepository,
        private readonly smsSender: ISMSSender
    ) { }

    /**
     * Generate a cryptographically random 6-digit OTP.
     */
    generateVerificationCode(): string {
        // Web Crypto API — synchronous, cryptographically secure, no imports needed
        const buf = new Uint32Array(1);
        globalThis.crypto.getRandomValues(buf);
        return String(buf[0] % 1_000_000).padStart(6, "0");
    }

    /**
     * Normalise a Ukrainian phone number to E.164 format (+380XXXXXXXXX).
     * Handles: 0XXXXXXXXX, 380XXXXXXXXX, +380XXXXXXXXX
     */
    normalizePhone(phone: string): string {
        let sanitized = phone.replace(/[^\d+]/g, "");

        if (sanitized.startsWith("0") && sanitized.length === 10) {
            sanitized = "+38" + sanitized;
        } else if (sanitized.startsWith("380") && sanitized.length === 12) {
            sanitized = "+" + sanitized;
        } else if (!sanitized.startsWith("+")) {
            sanitized = "+" + sanitized;
        }

        return sanitized;
    }

    /**
     * Validate E.164 phone format after normalisation.
     */
    validatePhone(phone: string): boolean {
        const normalized = this.normalizePhone(phone);
        return /^\+[1-9]\d{6,14}$/.test(normalized);
    }

    /**
     * Validate OTP format: exactly 6 numeric digits.
     */
    validateCode(code: string): boolean {
        return typeof code === "string" && code.length === 6 && /^\d+$/.test(code);
    }

    /**
     * Send OTP to the given phone number.
     */
    async sendVerificationCode(phone: string): Promise<void> {
        const normalizedPhone = this.normalizePhone(phone);

        if (!this.validatePhone(phone)) {
            throw AppError.badRequest("Invalid phone number format");
        }

        const code = this.generateVerificationCode();

        // Log masked phone only; never log the full number in production
        const maskedPhone = normalizedPhone.slice(0, 4) + "****" + normalizedPhone.slice(-2);
        this.log.info({ phone: maskedPhone }, "Sending OTP");

        // In dev, also log the code so engineers can test without SMS
        if (!this.isProd()) {
            this.log.debug({ code }, "[DEV] OTP code (dev mode only)");
        }

        const sent = await this.smsSender.sendVerificationCode(normalizedPhone, code);
        if (!sent) {
            throw AppError.internal("Failed to send SMS");
        }

        await this.verificationRepository.createPhoneVerification(normalizedPhone, code);
    }

    /**
     * Verify OTP and return (or create) the associated user.
     */
    async verifyPhone(phone: string, code: string): Promise<User> {
        if (!this.validateCode(code)) {
            throw AppError.badRequest("Invalid code format");
        }

        const normalizedPhone = this.normalizePhone(phone);

        const record = await this.verificationRepository.getLatestPhoneVerification(normalizedPhone);
        if (!record) {
            throw AppError.badRequest("No verification pending or code expired");
        }

        if (record.code !== code) {
            throw AppError.badRequest("Invalid verification code");
        }

        await this.verificationRepository.markPhoneVerified(record.id);

        let user = await this.userRepository.findByPhone(normalizedPhone);
        if (!user) {
            user = await this.userRepository.createWithPhone(normalizedPhone);
            this.log.info({ userId: user.id }, "New user created via phone verification");
        } else {
            this.log.info({ userId: user.id }, "User authenticated via phone");
        }

        return user;
    }

    async getUserById(userId: string): Promise<User | null> {
        return this.userRepository.findById(userId);
    }

    // ─── Dev helpers ───────────────────────────────────────────────────────────

    /** Only used by /api/auth/dev-login — guarded at the router level */
    async getOrCreateDevUser(): Promise<User> {
        if (this.isProd()) throw AppError.forbidden("Not available in production");

        const email = "dev@example.com";
        let user = await this.userRepository.findByEmail(email);
        if (!user) {
            user = await this.userRepository.create({
                email,
                firstName: "Dev",
                lastName: "Tester",
            });
            this.log.info({ userId: user.id }, "Dev user created");
        }
        return user;
    }

    private isProd(): boolean {
        return process.env.NODE_ENV === "production";
    }
}
