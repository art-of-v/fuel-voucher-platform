/**
 * Drizzle Package Repository Implementation
 * 
 * Concrete implementation of IPackageRepository using Drizzle ORM.
 */

import { eq } from 'drizzle-orm';
import { db } from '../../../../shared/database/db';
import { IPackageRepository } from '../../../../domain/repositories/package.repository';
import { FuelPackage, InsertFuelPackage, fuelPackages } from '../../../../shared/database/schema';

export class DrizzlePackageRepository implements IPackageRepository {
    async findAll(): Promise<FuelPackage[]> {
        return db.select().from(fuelPackages);
    }

    async findById(id: string): Promise<FuelPackage | null> {
        const results = await db
            .select()
            .from(fuelPackages)
            .where(eq(fuelPackages.id, id))
            .limit(1);

        return results[0] || null;
    }

    async findByStation(stationId: string): Promise<FuelPackage[]> {
        return db
            .select()
            .from(fuelPackages)
            .where(eq(fuelPackages.stationId, stationId));
    }

    async create(data: InsertFuelPackage): Promise<FuelPackage> {
        const results = await db
            .insert(fuelPackages)
            .values(data)
            .returning();

        return results[0];
    }

    async update(id: string, data: Partial<InsertFuelPackage>): Promise<FuelPackage | null> {
        const results = await db
            .update(fuelPackages)
            .set(data)
            .where(eq(fuelPackages.id, id))
            .returning();

        return results[0] || null;
    }

    async delete(id: string): Promise<boolean> {
        const results = await db
            .delete(fuelPackages)
            .where(eq(fuelPackages.id, id))
            .returning();

        return results.length > 0;
    }

    async deleteAll(): Promise<number> {
        const results = await db
            .delete(fuelPackages)
            .returning();

        return results.length;
    }
}
