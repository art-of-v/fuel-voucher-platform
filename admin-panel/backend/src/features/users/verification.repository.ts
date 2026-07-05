import { db } from "../../shared/database/db";
import { eq, and, gt, desc } from "drizzle-orm";
import { phoneVerifications, type PhoneVerification } from "../../shared/database/schema";

export const verificationRepository = {
    async createPhoneVerification(phone: string, code: string): Promise<PhoneVerification> {
        const expiresAt = new Date(Date.now() + 5 * 60 * 1000);
        const [verification] = await db
            .insert(phoneVerifications)
            .values({ phone, code, expiresAt })
            .returning();
        return verification;
    },

    async getLatestPhoneVerification(phone: string): Promise<PhoneVerification | undefined> {
        const [verification] = await db
            .select()
            .from(phoneVerifications)
            .where(
                and(
                    eq(phoneVerifications.phone, phone),
                    gt(phoneVerifications.expiresAt, new Date()),
                    eq(phoneVerifications.verified, 0)
                )
            )
            .orderBy(desc(phoneVerifications.createdAt))
            .limit(1);
        return verification;
    },

    async markPhoneVerified(id: number): Promise<void> {
        await db
            .update(phoneVerifications)
            .set({ verified: 1 })
            .where(eq(phoneVerifications.id, id));
    }
};
