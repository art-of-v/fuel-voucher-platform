
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { AuthService } from '../../src/application/services/auth.service';
import { AppError } from '../../src/shared/errors/app-error';

describe('Auth Service', () => {
    let authService: AuthService;
    const mockUserRepository = {
        findByPhone: vi.fn(),
        createWithPhone: vi.fn(),
        findById: vi.fn(),
        findByEmail: vi.fn(),
        create: vi.fn(),
    };
    const mockVerificationRepository = {
        createPhoneVerification: vi.fn(),
        getLatestPhoneVerification: vi.fn(),
        markPhoneVerified: vi.fn(),
    };
    const mockSmsSender = {
        sendVerificationCode: vi.fn(),
    };

    beforeEach(() => {
        vi.clearAllMocks();
        authService = new AuthService(
            mockUserRepository as any,
            mockVerificationRepository as any,
            mockSmsSender as any
        );
    });

    describe('sendVerificationCode', () => {
        it('should send code for valid phone', async () => {
            mockSmsSender.sendVerificationCode.mockResolvedValue(true);
            await authService.sendVerificationCode('+1234567890');
            expect(mockSmsSender.sendVerificationCode).toHaveBeenCalledWith('+1234567890', expect.any(String));
            expect(mockVerificationRepository.createPhoneVerification).toHaveBeenCalled();
        });

        it('should throw for invalid phone format', async () => {
            await expect(authService.sendVerificationCode('invalid')).rejects.toThrow(AppError);
        });

        it('should throw if SMS fails', async () => {
            mockSmsSender.sendVerificationCode.mockResolvedValue(false);
            await expect(authService.sendVerificationCode('+1234567890')).rejects.toThrow('Failed to send SMS');
        });
    });

    describe('verifyPhone', () => {
        const phone = '+1234567890';
        const code = '123456';
        const verification = { id: 1, code, phone, verified: false, expiresAt: new Date(Date.now() + 10000) };

        it('should verify existing user', async () => {
            mockVerificationRepository.getLatestPhoneVerification.mockResolvedValue(verification);
            mockUserRepository.findByPhone.mockResolvedValue({ id: 'u1', phone });

            const user = await authService.verifyPhone(phone, code);
            expect(user.id).toBe('u1');
            expect(mockVerificationRepository.markPhoneVerified).toHaveBeenCalledWith(1);
        });

        it('should create new user if not found', async () => {
            mockVerificationRepository.getLatestPhoneVerification.mockResolvedValue(verification);
            mockUserRepository.findByPhone.mockResolvedValue(null);
            mockUserRepository.createWithPhone.mockResolvedValue({ id: 'u2', phone });

            const user = await authService.verifyPhone(phone, code);
            expect(user.id).toBe('u2');
            expect(mockUserRepository.createWithPhone).toHaveBeenCalledWith(phone);
        });

        it('should throw if code invalid', async () => {
            await expect(authService.verifyPhone(phone, 'wrong')).rejects.toThrow('Invalid code format');
        });

        it('should throw if no verification found', async () => {
            mockVerificationRepository.getLatestPhoneVerification.mockResolvedValue(null);
            await expect(authService.verifyPhone(phone, code)).rejects.toThrow('No verification pending');
        });

        it('should throw if code mismatch', async () => {
            mockVerificationRepository.getLatestPhoneVerification.mockResolvedValue({ ...verification, code: '654321' });
            await expect(authService.verifyPhone(phone, code)).rejects.toThrow('Invalid verification code');
        });
    });
});
