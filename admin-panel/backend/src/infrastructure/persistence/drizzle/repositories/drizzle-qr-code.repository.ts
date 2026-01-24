/**
 * Drizzle QR Code Repository Implementation
 */

import { eq, and } from 'drizzle-orm';
import { db } from '../../../../shared/database/db';
import { IQrCodeRepository } from '../../../../domain/repositories/qr-code.repository';
import { QrCode, InsertQrCode, qrCodes } from '../../../../shared/database/schema';

export class DrizzleQrCodeRepository implements IQrCodeRepository {
    async findAll(): Promise<QrCode[]> {
        return db.select().from(qrCodes);
    }

    async findById(id: number): Promise<QrCode | null> {
        const results = await db
            .select()
            .from(qrCodes)
            .where(eq(qrCodes.id, id))
            .limit(1);
        return results[0] || null;
    }

    async findAvailable(criteria: {
        stationId?: string;
        fuelType?: string;
        liters?: number;
    }): Promise<QrCode[]> {
        const conditions = [eq(qrCodes.status, 'available')];

        if (criteria.stationId) {
            conditions.push(eq(qrCodes.stationId, criteria.stationId));
        }
        if (criteria.fuelType) {
            conditions.push(eq(qrCodes.fuelType, criteria.fuelType));
        }
        if (criteria.liters) {
            conditions.push(eq(qrCodes.liters, criteria.liters));
        }

        return db
            .select()
            .from(qrCodes)
            .where(and(...conditions));
    }

    async create(data: InsertQrCode): Promise<QrCode> {
        const results = await db
            .insert(qrCodes)
            .values(data)
            .returning();
        return results[0];
    }

    async createBulk(data: InsertQrCode[]): Promise<QrCode[]> {
        return db
            .insert(qrCodes)
            .values(data)
            .returning();
    }

    async update(id: number, data: Partial<InsertQrCode>): Promise<QrCode | null> {
        const results = await db
            .update(qrCodes)
            .set(data)
            .where(eq(qrCodes.id, id))
            .returning();
        return results[0] || null;
    }

    async delete(id: number): Promise<boolean> {
        const results = await db
            .delete(qrCodes)
            .where(eq(qrCodes.id, id))
            .returning();
        return results.length > 0;
    }

    async markAsSold(id: number, purchaseId: number): Promise<QrCode | null> {
        const results = await db
            .update(qrCodes)
            .set({ status: 'sold', purchaseId })
            .where(eq(qrCodes.id, id))
            .returning();
        return results[0] || null;
    }
}
