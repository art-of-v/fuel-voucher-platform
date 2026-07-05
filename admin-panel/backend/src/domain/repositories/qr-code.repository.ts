/**
 * QR Code Repository Interface
 * 
 * Defines contract for QR code inventory persistence operations.
 */

import { QrCode, InsertQrCode } from '../../shared/database/schema';

export interface IQrCodeRepository {
    /**
     * Find all QR codes
     */
    findAll(): Promise<QrCode[]>;

    /**
     * Find QR code by ID
     */
    findById(id: number): Promise<QrCode | null>;

    /**
     * Find available QR codes matching criteria
     */
    findAvailable(criteria: {
        stationId?: string;
        fuelType?: string;
        liters?: number;
    }): Promise<QrCode[]>;

    /**
     * Create new QR code
     */
    create(data: InsertQrCode): Promise<QrCode>;

    /**
     * Create multiple QR codes
     */
    createBulk(data: InsertQrCode[]): Promise<QrCode[]>;

    /**
     * Update QR code
     */
    update(id: number, data: Partial<InsertQrCode>): Promise<QrCode | null>;

    /**
     * Delete QR code
     */
    delete(id: number): Promise<boolean>;

    /**
     * Mark QR code as sold
     */
    markAsSold(id: number, purchaseId: number): Promise<QrCode | null>;
}
