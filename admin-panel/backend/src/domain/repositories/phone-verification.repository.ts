/**
 * Phone Verification Repository Interface
 * 
 * Defines contract for phone verification persistence operations.
 */

import { PhoneVerification, InsertPhoneVerification } from '../../shared/database/schema';

export interface IPhoneVerificationRepository {
    /**
     * Find verification by phone number
     */
    findByPhone(phone: string): Promise<PhoneVerification | null>;

    /**
     * Find latest unverified code for phone
     */
    findLatestUnverified(phone: string): Promise<PhoneVerification | null>;

    /**
     * Create new verification code
     */
    create(data: InsertPhoneVerification): Promise<PhoneVerification>;

    /**
     * Mark verification as verified
     */
    markAsVerified(id: number): Promise<PhoneVerification | null>;

    /**
     * Delete expired verifications
     */
    deleteExpired(): Promise<number>;

    /**
     * Delete verifications for phone
     */
    deleteByPhone(phone: string): Promise<number>;
}
