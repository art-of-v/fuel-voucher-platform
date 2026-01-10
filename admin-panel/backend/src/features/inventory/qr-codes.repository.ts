import { db } from "../../shared/database/db";
import { eq, and, desc } from "drizzle-orm";
import { qrCodes, purchases, type QrCode, type InsertQrCode } from "../../shared/database/schema";

export const qrCodesRepository = {
    async createQrCode(qrCode: InsertQrCode): Promise<QrCode> {
        const [created] = await db.insert(qrCodes).values(qrCode).returning();
        return created;
    },

    async getAvailableQrCode(stationId: string, fuelType: string, liters: number): Promise<QrCode | undefined> {
        const [qrCode] = await db
            .select()
            .from(qrCodes)
            .where(
                and(
                    eq(qrCodes.stationId, stationId),
                    eq(qrCodes.fuelType, fuelType),
                    eq(qrCodes.liters, liters),
                    eq(qrCodes.status, "available")
                )
            )
            .limit(1);
        return qrCode;
    },

    async markQrCodeAsSold(qrCodeId: number, purchaseId: number): Promise<void> {
        await db
            .update(qrCodes)
            .set({ status: "sold", purchaseId })
            .where(eq(qrCodes.id, qrCodeId));
    },

    async findAvailableQrCode(stationId: string, fuelType: string, liters: number): Promise<QrCode | undefined> {
        const [qrCode] = await db
            .select()
            .from(qrCodes)
            .where(
                and(
                    eq(qrCodes.stationId, stationId),
                    eq(qrCodes.fuelType, fuelType),
                    eq(qrCodes.liters, liters),
                    eq(qrCodes.status, "available")
                )
            )
            .limit(1);
        return qrCode;
    },

    async assignQrToPurchase(purchaseId: number, qrCodeId: number): Promise<void> {
        await db.update(qrCodes).set({ status: "sold", purchaseId }).where(eq(qrCodes.id, qrCodeId));
        await db.update(purchases).set({ qrCodeId, status: "delivered" }).where(eq(purchases.id, purchaseId));
    },

    async getAllQrCodes(): Promise<QrCode[]> {
        return await db.select().from(qrCodes).orderBy(desc(qrCodes.createdAt));
    },

    async deleteQrCode(id: number): Promise<void> {
        await db.delete(qrCodes).where(eq(qrCodes.id, id));
    },

    async updateQrCode(id: number, data: Partial<InsertQrCode>): Promise<QrCode> {
        const [updated] = await db.update(qrCodes).set(data).where(eq(qrCodes.id, id)).returning();
        return updated;
    }
};
