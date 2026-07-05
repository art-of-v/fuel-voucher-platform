/**
 * Drizzle Phone Verification Repository Implementation
 */

import { eq, and, lt, gt, desc } from 'drizzle-orm';
import { db } from '../../../../shared/database/db';
import { IPhoneVerificationRepository } from '../../../../domain/repositories/phone-verification.repository';
import { PhoneVerification, InsertPhoneVerification, phoneVerifications } from '../../../../shared/database/schema';

export class DrizzlePhoneVerificationRepository implements IPhoneVerificationRepository {
    async findByPhone(phone: string): Promise<PhoneVerification | null> {
        const results = await db
            .select()
            .from(phoneVerifications)
            .where(eq(phoneVerifications.phone, phone))
            .orderBy(desc(phoneVerifications.createdAt))
            .limit(1);
        return results[0] || null;
    }

    async findLatestUnverified(phone: string): Promise<PhoneVerification | null> {
        const now = new Date();
        const results = await db
            .select()
            .from(phoneVerifications)
            .where(and(
                eq(phoneVerifications.phone, phone),
                eq(phoneVerifications.verified, 0),
                gt(phoneVerifications.expiresAt, now)
            ))
            .orderBy(desc(phoneVerifications.createdAt))
            .limit(1);
        return results[0] || null;
    }

    async create(data: InsertPhoneVerification): Promise<PhoneVerification> {
        const results = await db
            .insert(phoneVerifications)
            .values(data)
            .returning();
        return results[0];
    }

    async markAsVerified(id: number): Promise<PhoneVerification | null> {
        const results = await db
            .update(phoneVerifications)
            .set({ verified: 1 })
            .where(eq(phoneVerifications.id, id))
            .returning();
        return results[0] || null;
    }

    async deleteExpired(): Promise<number> {
        const now = new Date();
        const results = await db
            .delete(phoneVerifications)
            .where(lt(phoneVerifications.expiresAt, now))
            .returning();
        return results.length;
    }

    async deleteByPhone(phone: string): Promise<number> {
        const results = await db
            .delete(phoneVerifications)
            .where(eq(phoneVerifications.phone, phone))
            .returning();
        return results.length;
    }
}
