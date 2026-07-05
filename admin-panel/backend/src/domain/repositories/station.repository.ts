/**
 * Station Repository Interface
 * 
 * Defines persistence operations for Station entities.
 */

import { IBaseRepository, PaginatedResult, PaginationOptions } from './base.repository';

/**
 * Station entity (domain representation)
 */
export interface Station {
    id: number;
    externalId: string;
    provider: string;
    name: string;
    address: string;
    latitude: number;
    longitude: number;
    fuelPrices: Record<string, number> | null;
    services: string[] | null;
    workingHours: string | null;
    lastUpdated: Date;
}

/**
 * Station creation data
 */
export interface CreateStationData {
    externalId: string;
    provider: string;
    name: string;
    address: string;
    latitude: number;
    longitude: number;
    fuelPrices?: Record<string, number>;
    services?: string[];
    workingHours?: string;
}

/**
 * Station filter options
 */
export interface StationFilterOptions {
    provider?: string;
    fuelType?: string;
    nearLocation?: {
        latitude: number;
        longitude: number;
        radiusKm: number;
    };
}

/**
 * Station Repository Interface
 */
export interface IStationRepository extends IBaseRepository<Station, number> {
    /**
     * Find station by external ID and provider
     */
    findByExternalId(provider: string, externalId: string): Promise<Station | null>;

    /**
     * Find stations by provider
     */
    findByProvider(provider: string): Promise<Station[]>;

    /**
     * Find stations near a location
     */
    findNearLocation(
        latitude: number,
        longitude: number,
        radiusKm: number,
        limit?: number,
    ): Promise<Station[]>;

    /**
     * Find stations with filters
     */
    findWithFilters(
        filters?: StationFilterOptions,
        pagination?: PaginationOptions,
    ): Promise<PaginatedResult<Station>>;

    /**
     * Upsert station (create or update by external ID)
     */
    upsert(data: CreateStationData): Promise<Station>;

    /**
     * Update fuel prices for a station
     */
    updateFuelPrices(
        id: number,
        fuelPrices: Record<string, number>,
    ): Promise<void>;

    /**
     * Get all unique providers
     */
    getProviders(): Promise<string[]>;

    /**
     * Get all fuel types available across stations
     */
    getAvailableFuelTypes(): Promise<string[]>;
}
