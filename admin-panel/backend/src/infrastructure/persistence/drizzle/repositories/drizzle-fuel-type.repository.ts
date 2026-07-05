/**
 * Drizzle Fuel Type Repository Implementation
 */

import { eq } from 'drizzle-orm';
import { db } from '../../../../shared/database/db';
import { IFuelTypeRepository } from '../../../../domain/repositories/fuel-type.repository';
import { FuelType, InsertFuelType, fuelTypes } from '../../../../shared/database/schema';

export class DrizzleFuelTypeRepository implements IFuelTypeRepository {
    async findAll(): Promise<FuelType[]> {
        return db.select().from(fuelTypes);
    }

    async findById(id: string): Promise<FuelType | null> {
        const results = await db
            .select()
            .from(fuelTypes)
            .where(eq(fuelTypes.id, id))
            .limit(1);
        return results[0] || null;
    }

    async findByStation(stationId: string): Promise<FuelType[]> {
        return db
            .select()
            .from(fuelTypes)
            .where(eq(fuelTypes.stationId, stationId));
    }

    async create(data: InsertFuelType): Promise<FuelType> {
        const results = await db
            .insert(fuelTypes)
            .values(data)
            .returning();
        return results[0];
    }

    async update(id: string, data: Partial<InsertFuelType>): Promise<FuelType | null> {
        const results = await db
            .update(fuelTypes)
            .set(data)
            .where(eq(fuelTypes.id, id))
            .returning();
        return results[0] || null;
    }

    async delete(id: string): Promise<boolean> {
        const results = await db
            .delete(fuelTypes)
            .where(eq(fuelTypes.id, id))
            .returning();
        return results.length > 0;
    }
}
