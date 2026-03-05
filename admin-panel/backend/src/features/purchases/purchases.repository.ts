import { db } from "../../shared/database/db";
import { eq, desc } from "drizzle-orm";
import { purchases, qrCodes, vouchers, type Purchase, type InsertPurchase, type QrCode, type Voucher } from "../../shared/database/schema";

export const purchasesRepository = {
    async createPurchase(purchase: InsertPurchase): Promise<Purchase> {
        const [created] = await db.insert(purchases).values(purchase).returning();
        return created;
    },

    async getPurchase(id: number): Promise<Purchase | undefined> {
        const [purchase] = await db.select().from(purchases).where(eq(purchases.id, id));
        return purchase;
    },

    async getPurchaseByMonobankInvoice(monobankInvoiceId: string): Promise<Purchase | undefined> {
        const [purchase] = await db
            .select()
            .from(purchases)
            .where(eq(purchases.monobankInvoiceId, monobankInvoiceId));
        return purchase;
    },

    async getPurchasesByUserId(userId: string): Promise<Purchase[]> {
        return await db
            .select()
            .from(purchases)
            .where(eq(purchases.sessionId, userId))
            .orderBy(purchases.createdAt);
    },

    async updatePurchaseStatus(id: number, status: string, monobankInvoiceId?: string, monobankStatus?: string, qrCodeId?: number, voucherId?: string): Promise<void> {
        const updates: any = { status };
        if (monobankInvoiceId !== undefined) {
            updates.monobankInvoiceId = monobankInvoiceId;
        }
        if (monobankStatus !== undefined) {
            updates.monobankStatus = monobankStatus;
        }
        if (qrCodeId !== undefined) {
            updates.qrCodeId = qrCodeId;
        }
        if (voucherId !== undefined) {
            updates.voucherId = voucherId;
        }
        await db.update(purchases).set(updates).where(eq(purchases.id, id));
    },

    async getPurchaseWithQrCode(purchaseId: number): Promise<(Purchase & { qrCode?: QrCode, voucher?: Voucher }) | undefined> {
        const [purchase] = await db.select().from(purchases).where(eq(purchases.id, purchaseId));
        if (!purchase) return undefined;

        let result: any = { ...purchase };

        if (purchase.qrCodeId) {
            const [qrCode] = await db.select().from(qrCodes).where(eq(qrCodes.id, purchase.qrCodeId));
            result.qrCode = qrCode;
        }

        if (purchase.voucherId) {
            const [voucher] = await db.select().from(vouchers).where(eq(vouchers.id, purchase.voucherId));
            result.voucher = voucher;

            // Backwards compatibility: verify if we can expose this as a 'qrCode' for the frontend
            if (voucher && !result.qrCode) {
                // Construct a QR URL using the public API used elsewhere
                const qrData = voucher.qrCodeData || `VOUCHER:${voucher.id}`;
                const qrUrl = `https://api.qrserver.com/v1/create-qr-code/?size=250x250&ecc=L&data=${encodeURIComponent(qrData)}`;

                result.qrCode = {
                    id: -1,
                    stationId: voucher.provider,
                    fuelType: voucher.fuelType,
                    liters: voucher.amount,
                    qrCodeUrl: qrUrl,
                    qrCodeData: qrData,
                    status: "sold",
                    createdAt: voucher.createdAt,
                    purchaseId: purchase.id
                };
            }
        }

        return result;
    },

    async getAllPurchases(): Promise<Purchase[]> {
        return await db.select().from(purchases).orderBy(desc(purchases.createdAt));
    }
};
