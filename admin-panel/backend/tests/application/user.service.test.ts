
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { UserService } from '../../src/application/services/user.service';
import { AppError } from '../../src/shared/errors/app-error';

describe('User Service', () => {
    let userService: UserService;
    const mockUserRepository = {
        findAll: vi.fn(),
        findById: vi.fn(),
        update: vi.fn(),
        findByReferralCode: vi.fn(),
    };
    const mockNotificationRepository = {
        createNotification: vi.fn(),
    };

    beforeEach(() => {
        vi.clearAllMocks();
        userService = new UserService(
            mockUserRepository as any,
            mockNotificationRepository as any
        );
    });

    describe('createReferralCode', () => {
        it('should create code if unique', async () => {
            mockUserRepository.findByReferralCode.mockResolvedValue(null);
            mockUserRepository.update.mockResolvedValue({ id: 'u1', referralCode: 'REF123' });

            const result = await userService.createReferralCode('u1', 'REF123');

            expect(mockUserRepository.update).toHaveBeenCalledWith('u1', { referralCode: 'REF123' });
            expect(result.referralCode).toBe('REF123');
        });

        it('should throw if code taken', async () => {
            mockUserRepository.findByReferralCode.mockResolvedValue({ id: 'u2' }); // exists
            await expect(userService.createReferralCode('u1', 'REF123')).rejects.toThrow(AppError);
        });
    });

    describe('redeemReferralCode', () => {
        const referrer = { id: 'u2', bonusBalance: 100, referralCode: 'REF123' };
        const referee = { id: 'u1', bonusBalance: 0, referredBy: null };

        it('should redeem successfully', async () => {
            mockUserRepository.findByReferralCode.mockResolvedValue(referrer);
            mockUserRepository.findById.mockResolvedValue(referee);

            await userService.redeemReferralCode('u1', 'REF123');

            // Verify referee updated
            expect(mockUserRepository.update).toHaveBeenCalledWith('u1', { referredBy: 'u2' });
            // Verify bonus credited (referrer +50, referee +20)
            expect(mockUserRepository.update).toHaveBeenCalledWith('u2', { bonusBalance: 150 });
            expect(mockUserRepository.update).toHaveBeenCalledWith('u1', { bonusBalance: 20 });

            // Notification
            expect(mockNotificationRepository.createNotification).toHaveBeenCalled();
        });

        it('should throw if code not found', async () => {
            mockUserRepository.findByReferralCode.mockResolvedValue(null);
            await expect(userService.redeemReferralCode('u1', 'INVALID')).rejects.toThrow('Referral code');
        });

        it('should throw if self referral', async () => {
            mockUserRepository.findByReferralCode.mockResolvedValue({ id: 'u1' }); // same id
            await expect(userService.redeemReferralCode('u1', 'REF123')).rejects.toThrow('own code');
        });

        it('should throw if already referred', async () => {
            mockUserRepository.findByReferralCode.mockResolvedValue(referrer);
            mockUserRepository.findById.mockResolvedValue({ ...referee, referredBy: 'u3' });

            await expect(userService.redeemReferralCode('u1', 'REF123')).rejects.toThrow('already redeemed');
        });
    });
});
