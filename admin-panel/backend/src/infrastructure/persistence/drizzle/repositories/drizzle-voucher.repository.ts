/**
 * Drizzle Voucher Repository
 * 
 * Concrete implementation of IVoucherRepository using Drizzle ORM.
 */

import { db } from '../../../../shared/database/db';
import { eq, and, desc, asc, sql, inArray, or } from 'drizzle-orm';
import { vouchers, type Voucher as DbVoucher } from '../../../../shared/database/schema';
import type {
    IVoucherRepository,
    Voucher,
    VoucherStatus,
    CreateVoucherData,
    VoucherFilterOptions,
    VoucherQueryResult,
    UserVoucher,
    InventoryItem
} from '../../../../domain/repositories/voucher.repository';
import type { PaginationOptions, SortOptions } from '../../../../domain/repositories/base.repository';
import { getFuelAliases } from '../../../../domain/services/fuel-matcher.service';
import { encryptionService } from '../../../../shared/services/encryption.service';

function mapToDomain(dbVoucher: DbVoucher): Voucher {
    return {
        id: dbVoucher.id,
        provider: dbVoucher.provider,
        externalId: dbVoucher.externalId,
        fuelType: dbVoucher.fuelType,
        fuelSubtype: dbVoucher.fuelSubtype,
        amount: dbVoucher.amount,
        unit: dbVoucher.unit,
        expirationDate: dbVoucher.expirationDate,
        status: dbVoucher.status as VoucherStatus,
        redemptionRules: dbVoucher.redemptionRules,
        imageUrl: dbVoucher.imageUrl,
        qrCodeData: dbVoucher.qrCodeData,
        originalFileName: dbVoucher.originalFileName,
        source: dbVoucher.source,
        importJobId: dbVoucher.importJobId,
        assignedToUserId: dbVoucher.assignedToUserId,
        purchaseId: dbVoucher.purchaseId,
        createdAt: dbVoucher.createdAt,
        updatedAt: dbVoucher.updatedAt,
    };
}

export class DrizzleVoucherRepository implements IVoucherRepository {
    constructor(private readonly _db: typeof db | any = db) { }

    async findById(id: string): Promise<Voucher | null> {
        const [voucher] = await this._db.select().from(vouchers).where(eq(vouchers.id, id));
        if (!voucher) return null;
        const mapped = mapToDomain(voucher);
        // Decrypt for detail view
        if (mapped.qrCodeData) {
            mapped.qrCodeData = encryptionService.decrypt(mapped.qrCodeData);
        }
        return mapped;
    }

    async findByExternalId(provider: string, externalId: string): Promise<Voucher | null> {
        const [voucher] = await this._db
            .select()
            .from(vouchers)
            .where(
                and(
                    eq(vouchers.provider, provider),
                    eq(vouchers.externalId, externalId)
                )
            );
        return voucher ? mapToDomain(voucher) : null;
    }

    async findWithFilters(
        filters?: VoucherFilterOptions,
        pagination?: PaginationOptions,
        sort?: SortOptions,
    ): Promise<VoucherQueryResult> {
        const conditions: any[] = [];
        if (filters?.status) {
            conditions.push(eq(vouchers.status, filters.status));
        }
        if (filters?.provider) {
            conditions.push(eq(vouchers.provider, filters.provider));
        }
        if (filters?.fuelType) {
            conditions.push(eq(vouchers.fuelType, filters.fuelType));
        }
        if (filters?.assignedToUserId) {
            conditions.push(eq(vouchers.assignedToUserId, filters.assignedToUserId));
        }
        if (filters?.amount) {
            conditions.push(eq(vouchers.amount, filters.amount));
        }
        if (filters?.expirationDate) {
            // Simplified date filtering: match the exact date stored. 
            // Vouchers usually have expirationDate as midnight of the last day.
            conditions.push(eq(vouchers.expirationDate, new Date(filters.expirationDate)));
        }

        const whereClause = conditions.length > 0 ? and(...conditions) : undefined;

        let query = this._db.select().from(vouchers);
        if (whereClause) {
            query = query.where(whereClause);
        }

        const sortField = sort?.field ?? 'createdAt';
        const sortDir = sort?.direction ?? 'desc';

        // Handle sorting
        let orderBy: any;
        if (sortDir === 'asc') {
            orderBy = asc((vouchers as any)[sortField]);
        } else {
            orderBy = desc((vouchers as any)[sortField]);
        }
        query = query.orderBy(orderBy);

        if (pagination) {
            query = query.limit(pagination.limit).offset(pagination.offset);
        }

        const data = await query;

        const totalResult = await this._db
            .select({ count: sql<number>`count(*)` })
            .from(vouchers)
            .where(whereClause);
        const total = Number(totalResult[0]?.count ?? 0);

        const globalTotalResult = await this._db
            .select({ count: sql<number>`count(*)` })
            .from(vouchers);
        const globalTotal = Number(globalTotalResult[0]?.count ?? 0);

        const fuelTypesResult = await this._db
            .selectDistinct({ fuelType: vouchers.fuelType })
            .from(vouchers);
        const fuelTypes = fuelTypesResult.map((r: any) => r.fuelType).filter(Boolean);

        const providersResult = await this._db
            .selectDistinct({ provider: vouchers.provider })
            .from(vouchers);
        const providers = providersResult.map((r: any) => r.provider).filter(Boolean);

        const statusesResult = await this._db
            .selectDistinct({ status: vouchers.status })
            .from(vouchers);
        const statuses = statusesResult.map((r: any) => r.status).filter(Boolean);

        const amountsResult = await this._db
            .selectDistinct({ amount: vouchers.amount })
            .from(vouchers);
        const amounts = amountsResult.map((r: any) => r.amount).filter((a: any) => a !== null);

        return {
            data: data.map(mapToDomain),
            total,
            globalTotal,
            fuelTypes,
            providers,
            statuses,
            amounts,
        };
    }

    async findByUserId(userId: string): Promise<UserVoucher[]> {
        const result = await this._db
            .select({
                id: vouchers.id,
                provider: vouchers.provider,
                externalId: vouchers.externalId,
                fuelType: vouchers.fuelType,
                amount: vouchers.amount,
                status: vouchers.status,
                unit: vouchers.unit,
                qrCodeData: vouchers.qrCodeData,
                imageUrl: vouchers.imageUrl,
            })
            .from(vouchers)
            .where(eq(vouchers.assignedToUserId, userId))
            .orderBy(desc(vouchers.createdAt));

        return result.map((v: any) => ({
            id: v.id,
            provider: v.provider,
            externalId: v.externalId,
            fuelType: v.fuelType,
            amount: v.amount,
            status: v.status as VoucherStatus,
            unit: v.unit,
            // Decrypt for user view
            qrCodeData: v.qrCodeData ? encryptionService.decrypt(v.qrCodeData) : null,
            qrCodeUrl: v.imageUrl,
            imageUrl: v.imageUrl,
        }));
    }

    async getInventoryAggregation(): Promise<InventoryItem[]> {
        const result = await this._db
            .select({
                provider: vouchers.provider,
                fuelType: vouchers.fuelType,
                liters: vouchers.amount,
                availableCount: sql<number>`count(*)`,
            })
            .from(vouchers)
            .where(
                or(
                    eq(vouchers.status, 'available'),
                    eq(vouchers.status, 'imported')
                )
            )
            .groupBy(vouchers.provider, vouchers.fuelType, vouchers.amount);

        return result.map((r: any) => ({
            provider: r.provider,
            fuelType: r.fuelType,
            liters: r.liters,
            availableCount: Number(r.availableCount),
        }));
    }

    async findAvailable(
        provider: string,
        fuelType: string,
        liters: number,
    ): Promise<Voucher | null> {
        const aliases = getFuelAliases(fuelType);

        const [voucher] = await this._db
            .select()
            .from(vouchers)
            .where(
                and(
                    eq(vouchers.provider, provider),
                    inArray(vouchers.fuelType, aliases),
                    eq(vouchers.amount, liters),
                    or(
                        eq(vouchers.status, 'available'),
                        eq(vouchers.status, 'imported')
                    )
                )
            )
            .orderBy(asc(vouchers.createdAt))
            .limit(1);

        return voucher ? mapToDomain(voucher) : null;
    }

    async assignToPurchase(
        _purchaseId: number,
        userId: string,
        provider: string,
        fuelType: string,
        liters: number,
        quantity: number,
    ): Promise<Voucher[]> {
        const aliases = getFuelAliases(fuelType);
        const assigned: DbVoucher[] = [];

        await this._db.transaction(async (tx: any) => {
            for (let i = 0; i < quantity; i++) {
                const [voucher] = await tx
                    .select()
                    .from(vouchers)
                    .where(
                        and(
                            eq(vouchers.provider, provider),
                            inArray(vouchers.fuelType, aliases),
                            eq(vouchers.amount, liters),
                            or(
                                eq(vouchers.status, 'available'),
                                eq(vouchers.status, 'imported')
                            )
                        )
                    )
                    .orderBy(asc(vouchers.createdAt))
                    .limit(1)
                    .for('update');

                if (!voucher) {
                    throw new Error(`Insufficient inventory: only ${i} of ${quantity} vouchers available`);
                }

                const [updated] = await tx
                    .update(vouchers)
                    .set({
                        status: 'sold',
                        assignedToUserId: userId,
                        updatedAt: new Date(),
                    })
                    .where(eq(vouchers.id, voucher.id))
                    .returning();

                assigned.push(updated);
            }
        });

        return assigned.map(mapToDomain);
    }

    async findAll(): Promise<Voucher[]> {
        const result = await this._db.select().from(vouchers);
        return result.map(mapToDomain);
    }

    async create(data: CreateVoucherData, throwOnDuplicate: boolean = false): Promise<Voucher> {
        if (data.externalId) {
            const existing = await this.findByExternalId(data.provider, data.externalId);
            if (existing) {
                if (throwOnDuplicate) {
                    throw new Error(`Voucher with externalId ${data.externalId} already exists for provider ${data.provider}`);
                }
                return existing;
            }
        }

        const [voucher] = await this._db.insert(vouchers).values({
            provider: data.provider,
            externalId: data.externalId,
            fuelType: data.fuelType,
            fuelSubtype: data.fuelSubtype,
            amount: data.amount,
            unit: data.unit ?? 'liters',
            expirationDate: data.expirationDate,
            status: data.status ?? 'imported',
            redemptionRules: data.redemptionRules,
            qrCodeData: data.qrCodeData,
            originalFileName: data.originalFileName,
            source: data.source,
            importJobId: data.importJobId,
        }).returning();

        return mapToDomain(voucher);
    }

    async update(id: string, data: Partial<Voucher>): Promise<Voucher> {
        const [voucher] = await this._db
            .update(vouchers)
            .set({ ...data, updatedAt: new Date() })
            .where(eq(vouchers.id, id))
            .returning();
        return mapToDomain(voucher);
    }

    async save(entity: Voucher): Promise<Voucher> {
        return this.update(entity.id, entity);
    }

    async delete(id: string): Promise<void> {
        await this._db.delete(vouchers).where(eq(vouchers.id, id));
    }

    async deleteAll(): Promise<void> {
        await this._db.delete(vouchers);
    }

    async count(): Promise<number> {
        const result = await this._db.select().from(vouchers);
        return result.length;
    }

    async exists(id: string): Promise<boolean> {
        const [voucher] = await this._db.select({ id: vouchers.id }).from(vouchers).where(eq(vouchers.id, id));
        return !!voucher;
    }
}

export const drizzleVoucherRepository = new DrizzleVoucherRepository();
