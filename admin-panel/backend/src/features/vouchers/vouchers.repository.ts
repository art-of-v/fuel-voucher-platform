import { db } from "../../shared/database/db";
import { eq, and, desc, asc, sql, inArray, or } from "drizzle-orm";
import { vouchers, type Voucher, type InsertVoucher } from "../../shared/database/schema";
import { encryptionService } from "../../shared/services/encryption.service";

export function getFuelAliases(type: string): string[] {
    const t = type.toLowerCase().trim();
    const set = new Set([type]);

    // Handle Pulls (Premium) fuels
    if (t.includes('pulls') || t.includes('pills') || t.includes('пульс') || t.includes('fulls')) {
        if (t.includes('diesel') || t.includes('дп') || t.includes('dp')) {
            set.add('Diesel Pulls');
            set.add('ДП Pulls');
            set.add('ДП PULLS');
            set.add('ДП ПУЛЬС');
            set.add('Diesel ПУЛЬС');
            set.add('Pulls Diesel');
            // OCR common error
            set.add('ДП FULLS');
        } else {
            set.add('A-95 Pulls');
            set.add('Pulls 95');
            set.add('A-95 ПУЛЬС');
            set.add('А-95 ПУЛЬС');
        }
    }

    // Handle Mustang (WOG)
    if (t.includes('mustang')) {
        if (t.includes('diesel') || t.includes('дп') || t.includes('dp')) {
            set.add('Diesel Mustang');
            set.add('ДП Mustang');
            set.add('ДТ Mustang');
        } else {
            set.add('A-95 Mustang');
            set.add('Mustang 95');
        }
    }

    // Handle UPG-100
    if (t.includes('upg-100') || t.includes('100')) {
        set.add('UPG-100');
        set.add('100');
    }

    // Generic Diesel/DP matching
    if (t.includes('diesel') || t.includes('дп') || t.includes('dp')) {
        set.add('Diesel');
        set.add('ДП');
        set.add('ДП ЄВРО');
        set.add('ДП ЕВРО');
        set.add('DP');
        set.add('diesel');
        set.add('ГП'); // Gaz-Petrol (sometimes used for diesel in UA)
        set.add('ДТ'); // Diesel Toplivo
    }

    // Generic 95 matching
    if (t.includes('95')) {
        set.add('A-95');
        set.add('А-95');
        set.add('A-95 ЄВРО');
        set.add('А-95 ЄВРО');
        set.add('A-95 ЕВРО');
        set.add('А-95 ЕВРО');
        set.add('95');
    }

    return Array.from(set);
}

export function getProviderAliases(provider: string): string[] {
    const p = provider.toLowerCase().trim();
    const set = new Set([provider]);

    if (p === 'okko' || p === 'окко') {
        set.add('OKKO');
        set.add('ОККО'); // Cyrillic
    } else if (p === 'wog' || p === 'вог') {
        set.add('WOG');
        set.add('ВОГ');
    } else if (p === 'upg' || p === 'юпджі') {
        set.add('UPG');
        set.add('ЮПДЖІ');
    } else if (p === 'shell' || p === 'шелл') {
        set.add('SHELL');
        set.add('ШЕЛЛ');
    }

    return Array.from(set);
}

export const vouchersRepository = {
    async createVoucher(voucher: InsertVoucher, throwOnDuplicate: boolean = false): Promise<Voucher> {
        if (voucher.externalId) {
            console.log(`[STORAGE] Checking existence for ${voucher.provider}:${voucher.externalId}`);
            const existing = await db
                .select()
                .from(vouchers)
                .where(
                    and(
                        eq(vouchers.externalId, voucher.externalId),
                        eq(vouchers.provider, voucher.provider || "OKKO")
                    )
                )
                .limit(1);

            if (existing.length > 0) {
                console.log(`[STORAGE] Found existing voucher: ${existing[0].id} (Idempotent return)`);

                if (throwOnDuplicate) {
                    throw new Error(`DUPLICATE: Voucher with externalId ${voucher.externalId} already exists`);
                }

                // Update QR code if missing in existing but present in new
                if (!existing[0].qrCodeData && voucher.qrCodeData) {
                    await db.update(vouchers)
                        .set({ qrCodeData: voucher.qrCodeData, updatedAt: new Date() })
                        .where(eq(vouchers.id, existing[0].id));

                    return { ...existing[0], qrCodeData: voucher.qrCodeData };
                }

                return existing[0];
            }
        }

        const [created] = await db.insert(vouchers).values(voucher).returning();
        return created;
    },

    async getVouchers(filters: { status?: string, provider?: string, fuelType?: string, limit?: number, offset?: number, sortBy?: string, sortDirection?: 'asc' | 'desc' } = {}): Promise<{ data: Voucher[], total: number, globalTotal: number, fuelTypes: string[] }> {
        let conditions = [];
        if (filters.status) conditions.push(eq(vouchers.status, filters.status));
        if (filters.provider) conditions.push(eq(vouchers.provider, filters.provider));
        if (filters.fuelType) conditions.push(eq(vouchers.fuelType, filters.fuelType));

        const whereClause = conditions.length > 0 ? and(...conditions) : undefined;

        let orderBy = desc(vouchers.createdAt);
        if (filters.sortBy) {
            const col = filters.sortBy;
            const dir = filters.sortDirection === 'asc' ? asc : desc;
            switch (col) {
                case 'amount': orderBy = dir(vouchers.amount); break;
                case 'fuelType': orderBy = dir(vouchers.fuelType); break;
                case 'provider': orderBy = dir(vouchers.provider); break;
                case 'expirationDate': orderBy = dir(vouchers.expirationDate); break;
                case 'externalId': orderBy = dir(vouchers.externalId); break;
                case 'status': orderBy = dir(vouchers.status); break;
                case 'createdAt': orderBy = dir(vouchers.createdAt); break;
                case 'id': orderBy = dir(vouchers.id); break;
            }
        }

        const data = await db
            .select()
            .from(vouchers)
            .where(whereClause)
            .limit(filters.limit || 50)
            .offset(filters.offset || 0)
            .orderBy(orderBy);

        const [countResult] = await db.select({ count: sql<number>`count(*)`.mapWith(Number) }).from(vouchers).where(whereClause);
        const total = countResult ? countResult.count : 0;

        const [globalCountResult] = await db.select({ count: sql<number>`count(*)`.mapWith(Number) }).from(vouchers);
        const globalTotal = globalCountResult ? globalCountResult.count : 0;

        const fuelTypesResult = await db.selectDistinct({ fuelType: vouchers.fuelType }).from(vouchers);
        const fuelTypes = fuelTypesResult.map((r: any) => r.fuelType).filter(Boolean) as string[];

        return { data, total, globalTotal, fuelTypes };
    },

    async getVoucherById(id: string): Promise<Voucher | undefined> {
        const [voucher] = await db.select().from(vouchers).where(eq(vouchers.id, id));

        if (voucher && voucher.qrCodeData) {
            // Decrypt for detail view (Admin Panel Scan Modal)
            return {
                ...voucher,
                qrCodeData: encryptionService.decrypt(voucher.qrCodeData)
            };
        }

        return voucher;
    },

    async getVoucherByExternalId(provider: string, externalId: string): Promise<Voucher | undefined> {
        const [voucher] = await db
            .select()
            .from(vouchers)
            .where(
                and(
                    eq(vouchers.provider, provider),
                    eq(vouchers.externalId, externalId)
                )
            );
        return voucher;
    },

    async updateVoucher(id: string, data: Partial<Voucher>): Promise<Voucher> {
        const [updated] = await db
            .update(vouchers)
            .set({ ...data, updatedAt: new Date() })
            .where(eq(vouchers.id, id))
            .returning();
        return updated;
    },

    async deleteVoucher(id: string): Promise<void> {
        await db.delete(vouchers).where(eq(vouchers.id, id));
    },

    async deleteAllVouchers(): Promise<void> {
        // Drizzle requires explicit WHERE or raw SQL for delete all
        await db.execute(sql`DELETE FROM ${vouchers}`);
    },

    async getAvailableVouchers(): Promise<Voucher[]> {
        return await db.select().from(vouchers).where(eq(vouchers.status, "available")).orderBy(desc(vouchers.createdAt));
    },

    async findAvailableVoucher(stationName: string, fuelType: string, liters: number): Promise<Voucher | undefined> {
        // Note: strict matching on provider/fuelType/amount
        // We assume 'imported' status means available for sale.

        // Normalize fuel type for matching
        let fuelVariants = getFuelAliases(fuelType);

        const [voucher] = await db
            .select()
            .from(vouchers)
            .where(
                and(
                    // Allow loose matching on provider if needed, but for now strict on name is safer
                    // eq(vouchers.provider, stationName), 
                    // Actually, vouchers often have provider as "OKKO" or "WOG"
                    sql`lower(${vouchers.provider}) = ${stationName.toLowerCase()}`,

                    // Match any of the fuel variants
                    inArray(vouchers.fuelType, fuelVariants),

                    eq(vouchers.amount, liters),

                    // Check for both 'imported' and 'available' statuses
                    or(
                        eq(vouchers.status, "imported"),
                        eq(vouchers.status, "available")
                    )
                )
            )
            .limit(1);
        return voucher;
    },

    async assignVoucherToPurchase(purchaseId: number, voucherId: string): Promise<void> {
        // 1. Mark voucher as sold
        await db.update(vouchers)
            .set({ status: "sold" })
            .where(eq(vouchers.id, voucherId));

        // 2. Link voucher to purchase record
        const { purchases } = await import("../../shared/database/schema");
        await db.update(purchases)
            .set({ voucherId })
            .where(eq(purchases.id, purchaseId));
    },

    async getInventoryAggregation(): Promise<{ provider: string, fuelType: string, liters: number, availableCount: number }[]> {
        const result = await db
            .select({
                provider: vouchers.provider,
                fuelType: vouchers.fuelType,
                liters: vouchers.amount,
                availableCount: sql<number>`count(*)`.mapWith(Number),
            })
            .from(vouchers)
            .where(
                or(
                    eq(vouchers.status, "imported"),
                    eq(vouchers.status, "available")
                )
            )
            .groupBy(vouchers.provider, vouchers.fuelType, vouchers.amount);
        return result;
    },

    async assignVouchersToPurchase(
        purchaseId: number,
        userId: string,
        provider: string,
        fuelType: string,
        liters: number,
        quantity: number
    ): Promise<Voucher[]> {
        return await db.transaction(async (tx: any) => {
            const available = await tx
                .select()
                .from(vouchers)
                .where(
                    and(
                        eq(vouchers.amount, liters),
                        or(
                            eq(vouchers.status, "imported"),
                            eq(vouchers.status, "available")
                        ),
                        inArray(vouchers.provider, getProviderAliases(provider)),
                        inArray(vouchers.fuelType, getFuelAliases(fuelType))
                    )
                )
                .limit(quantity)
                .for("update", { skipLocked: true });

            if (available.length < quantity) {
                throw new Error(
                    `Insufficient inventory for ${provider} ${fuelType} ${liters}L. Requested: ${quantity}, Found: ${available.length}`
                );
            }

            await tx
                .update(vouchers)
                .set({
                    status: "sold",
                    assignedToUserId: userId,
                    purchaseId: purchaseId > 0 ? purchaseId : null,
                    updatedAt: new Date(),
                })
                .where(inArray(vouchers.id, available.map((v: any) => v.id)));

            return available;
        });
    },

    async getUserVouchers(userId: string): Promise<(Pick<Voucher, 'id' | 'provider' | 'fuelType' | 'amount' | 'status' | 'unit' | 'externalId'> & { qrCodeUrl?: string; qrCodeData?: string })[]> {
        const result = await db
            .select({
                id: vouchers.id,
                provider: vouchers.provider,
                externalId: vouchers.externalId,
                fuelType: vouchers.fuelType,
                amount: vouchers.amount,
                status: vouchers.status,
                unit: vouchers.unit,
                qrCodeData: vouchers.qrCodeData
            })
            .from(vouchers)
            .where(eq(vouchers.assignedToUserId, userId));

        return result.map((v: any) => {
            const rawQrData = v.qrCodeData ? encryptionService.decrypt(v.qrCodeData) : null;
            return {
                id: v.id,
                provider: v.provider,
                externalId: v.externalId,
                fuelType: v.fuelType,
                amount: v.amount,
                status: v.status,
                unit: v.unit,
                qrCodeData: rawQrData,
                qrCodeUrl: rawQrData
                    ? `https://api.qrserver.com/v1/create-qr-code/?size=250x250&ecc=L&data=${encodeURIComponent(rawQrData)}`
                    : undefined
            };
        });
    }
};
