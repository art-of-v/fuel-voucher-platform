/**
 * Fuel Type Repository Interface
 * 
 * Defines contract for fuel type persistence operations.
 */

import { FuelType, InsertFuelType } from '../../shared/database/schema';

export interface IFuelTypeRepository {
    /**
     * Find all fuel types
     */
    findAll(): Promise<FuelType[]>;

    /**
     * Find fuel type by ID
     */
    findById(id: string): Promise<FuelType | null>;

    /**
     * Find fuel types by station
     */
    findByStation(stationId: string): Promise<FuelType[]>;

    /**
     * Create new fuel type
     */
    create(data: InsertFuelType): Promise<FuelType>;

    /**
     * Update fuel type
     */
    update(id: string, data: Partial<InsertFuelType>): Promise<FuelType | null>;

    /**
     * Delete fuel type
     */
    delete(id: string): Promise<boolean>;
}
