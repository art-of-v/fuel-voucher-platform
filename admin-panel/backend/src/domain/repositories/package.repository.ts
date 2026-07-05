/**
 * Package Repository Interface
 * 
 * Defines contract for fuel package persistence operations.
 */

import { FuelPackage, InsertFuelPackage } from '../../shared/database/schema';

export interface IPackageRepository {
    /**
     * Find all packages
     */
    findAll(): Promise<FuelPackage[]>;

    /**
     * Find package by ID
     */
    findById(id: string): Promise<FuelPackage | null>;

    /**
     * Find packages by station
     */
    findByStation(stationId: string): Promise<FuelPackage[]>;

    /**
     * Create new package
     */
    create(data: InsertFuelPackage): Promise<FuelPackage>;

    /**
     * Update package
     */
    update(id: string, data: Partial<InsertFuelPackage>): Promise<FuelPackage | null>;

    /**
     * Delete package
     */
    delete(id: string): Promise<boolean>;

    /**
     * Delete all packages
     */
    deleteAll(): Promise<number>;
}
