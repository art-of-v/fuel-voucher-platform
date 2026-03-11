import { IUserRepository, User } from "../../domain/repositories/user.repository";
import { IDeviceRepository, CreateDeviceData, Device } from "../../domain/repositories/device.repository";
import { IDeviceSessionRepository } from "../../domain/repositories/device-session.repository";
import { AppError } from "../../shared/errors/app-error";
import { logger } from "../../infrastructure/logging/logger";
import { CryptoService } from "../../shared/services/crypto.service";
import { getRedisClient } from "../../shared/infrastructure/redis";

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
        private readonly smsSender: ISMSSender,
        private readonly deviceRepository: IDeviceRepository,
        private readonly sessionRepository: IDeviceSessionRepository
    ) { }

    /**
     * Generate a cryptographically random 6-digit OTP.
     */
    generateVerificationCode(): string {
        if (process.env.SMS_PROVIDER === "dev" || !process.env.TWILIO_ACCOUNT_SID) {
            return "000000";
        }
        const buf = new Uint32Array(1);
        globalThis.crypto.getRandomValues(buf);
        return String(buf[0] % 1_000_000).padStart(6, "0");
    }

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

    validatePhone(phone: string): boolean {
        const normalized = this.normalizePhone(phone);
        return /^\+[1-9]\d{6,14}$/.test(normalized);
    }

    validateCode(code: string): boolean {
        return typeof code === "string" && code.length === 6 && /^\d+$/.test(code);
    }

    async sendVerificationCode(phone: string): Promise<void> {
        const normalizedPhone = this.normalizePhone(phone);
        if (!this.validatePhone(phone)) {
            throw AppError.badRequest("Invalid phone number format");
        }
        const code = this.generateVerificationCode();
        const sent = await this.smsSender.sendVerificationCode(normalizedPhone, code);
        if (!sent && this.isProd()) {
            throw AppError.internal("Failed to send SMS");
        }
        await this.verificationRepository.createPhoneVerification(normalizedPhone, code);
    }

    async verifyPhone(phone: string, code: string): Promise<User> {
        if (!this.validateCode(code)) throw AppError.badRequest("Invalid code format");
        const normalizedPhone = this.normalizePhone(phone);
        const record = await this.verificationRepository.getLatestPhoneVerification(normalizedPhone);
        if (!record || record.code !== code) throw AppError.unauthorized("Invalid or expired code");

        await this.verificationRepository.markPhoneVerified(record.id);

        let user = await this.userRepository.findByPhone(normalizedPhone);
        if (!user) {
            user = await this.userRepository.createWithPhone(normalizedPhone);
            this.log.info({ userId: user.id }, "New user created");
        }
        return user;
    }

    async registerDevice(data: CreateDeviceData): Promise<Device> {
        return this.deviceRepository.registerDevice(data);
    }

    async generateChallenge(deviceId: string): Promise<string> {
        const device = await this.deviceRepository.findByDeviceId(deviceId);
        if (!device || device.status !== 'ACTIVE') throw AppError.unauthorized("Device not found or inactive");

        const challenge = CryptoService.generateChallenge();
        const redis = getRedisClient();
        await redis.setex(`challenge:${deviceId}`, 30, challenge);
        return challenge;
    }

    async verifyDeviceChallenge(deviceId: string, challenge: string, signature: string): Promise<void> {
        const redis = getRedisClient();
        const storedChallenge = await redis.get(`challenge:${deviceId}`);
        if (!storedChallenge || storedChallenge !== challenge) throw AppError.unauthorized("Challenge invalid or expired");
 
        const device = await this.deviceRepository.findByDeviceId(deviceId);
        if (!device || device.status !== 'ACTIVE') throw AppError.unauthorized("Device inactive or not found");
 
        const isValid = CryptoService.verifySignature(challenge, signature, device.publicKey);
        if (!isValid) throw AppError.unauthorized("Invalid signature");
 
        await redis.del(`challenge:${deviceId}`);
        await this.deviceRepository.updateLastSeen(deviceId);
    }

    async logout(deviceId: string): Promise<void> {
        // 1. Revoke all sessions for the device from DB
        await this.sessionRepository.revokeAllForDevice(deviceId);
        
        // 2. Mark device as inactive if hard logout (optional, but safer)
        // await this.deviceRepository.updateStatus(deviceId, 'REVOKED');
 
        this.log.info({ deviceId }, 'Device sessions cleared on logout');
    }

    async revokeDevice(deviceId: string): Promise<void> {
        // Revoke all sessions for the device
        await this.sessionRepository.revokeAllForDevice(deviceId);
        this.log.info({ deviceId }, 'Device revoked, all sessions cleared');
    }

    async getUserById(userId: string): Promise<User | null> {
        return this.userRepository.findById(userId);
    }

    private isProd(): boolean {
        return process.env.NODE_ENV === "production";
    }
}
