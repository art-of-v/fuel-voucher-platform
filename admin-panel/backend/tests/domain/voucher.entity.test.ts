
import { describe, it, expect } from 'vitest';
import { VoucherEntity, VoucherProps } from '../../src/domain/entities/voucher.entity';

describe('Voucher Entity', () => {
    const defaultProps: VoucherProps = {
        id: 'voucher-123',
        provider: 'UPG',
        externalId: 'EXT-001',
        fuelType: 'A-95',
        fuelSubtype: null,
        amount: 20,
        unit: 'L',
        expirationDate: new Date('2030-01-01T00:00:00Z'),
        status: 'available',
        redemptionRules: null,
        imageUrl: null,
        qrCodeData: 'QR-DATA',
        originalFileName: 'import.pdf',
        source: 'import',
        importJobId: 'job-1',
        assignedToUserId: null,
        purchaseId: null,
        createdAt: new Date('2023-01-01T10:00:00Z'),
        updatedAt: new Date('2023-01-01T10:00:00Z'),
    };

    it('should create a voucher entity correctly', () => {
        const voucher = VoucherEntity.create(defaultProps);
        expect(voucher).toBeInstanceOf(VoucherEntity);
        expect(voucher.id).toBe(defaultProps.id);
        expect(voucher.provider).toBe(defaultProps.provider);
        expect(voucher.status).toBe('available');
    });

    it('should check availability', () => {
        const available = VoucherEntity.create({ ...defaultProps, status: 'available' });
        expect(available.isAvailable()).toBe(true);
        expect(available.isAssigned()).toBe(false);

        const imported = VoucherEntity.create({ ...defaultProps, status: 'imported' });
        expect(imported.isAvailable()).toBe(true);

        const sold = VoucherEntity.create({ ...defaultProps, status: 'sold' });
        expect(sold.isAvailable()).toBe(false);
    });

    it('should check assignment status', () => {
        const assigned = VoucherEntity.create({ ...defaultProps, assignedToUserId: 'user-1' });
        expect(assigned.isAssigned()).toBe(true);

        const unassigned = VoucherEntity.create({ ...defaultProps, assignedToUserId: null });
        expect(unassigned.isAssigned()).toBe(false);
    });

    it('should check sold status', () => {
        const sold = VoucherEntity.create({ ...defaultProps, status: 'sold' });
        expect(sold.isSold()).toBe(true);
    });

    it('should check expiration', () => {
        const futureDate = new Date();
        futureDate.setFullYear(futureDate.getFullYear() + 1);
        const valid = VoucherEntity.create({ ...defaultProps, expirationDate: futureDate });
        expect(valid.isExpired()).toBe(false);

        const pastDate = new Date();
        pastDate.setFullYear(pastDate.getFullYear() - 1);
        const expired = VoucherEntity.create({ ...defaultProps, expirationDate: pastDate });
        expect(expired.isExpired()).toBe(true);

        const noExpo = VoucherEntity.create({ ...defaultProps, expirationDate: null });
        expect(noExpo.isExpired()).toBe(false);
    });

    it('should determine if assignable', () => {
        const futureDate = new Date();
        futureDate.setFullYear(futureDate.getFullYear() + 1);

        const assignable = VoucherEntity.create({
            ...defaultProps,
            status: 'available',
            assignedToUserId: null,
            expirationDate: futureDate
        });
        expect(assignable.canBeAssigned()).toBe(true);

        const alreadyAssigned = VoucherEntity.create({ ...defaultProps, assignedToUserId: 'u1' });
        expect(alreadyAssigned.canBeAssigned()).toBe(false);

        const wrongStatus = VoucherEntity.create({ ...defaultProps, status: 'sold' });
        expect(wrongStatus.canBeAssigned()).toBe(false);

        const pastDate = new Date();
        pastDate.setFullYear(pastDate.getFullYear() - 1);
        const expired = VoucherEntity.create({ ...defaultProps, expirationDate: pastDate });
        expect(expired.canBeAssigned()).toBe(false);
    });

    it('should match criteria correctly case-insensitive', () => {
        const voucher = VoucherEntity.create(defaultProps); // UPG, A-95, 20L

        expect(voucher.matchesCriteria('UPG', 'A-95', 20)).toBe(true);
        expect(voucher.matchesCriteria('upg', 'a-95', 20)).toBe(true); // Case
        expect(voucher.matchesCriteria('OKKO', 'A-95', 20)).toBe(false); // Provider
        expect(voucher.matchesCriteria('UPG', 'Diesel', 20)).toBe(false); // Fuel
        expect(voucher.matchesCriteria('UPG', 'A-95', 10)).toBe(false); // Amount
    });

    it('should return display name', () => {
        const voucher = VoucherEntity.create(defaultProps);
        expect(voucher.getDisplayName()).toBe('UPG A-95 20L');
    });

    it('should expose all properties via getters', () => {
        const voucher = VoucherEntity.create(defaultProps);
        expect(voucher.id).toBe(defaultProps.id);
        expect(voucher.externalId).toBe(defaultProps.externalId);
        expect(voucher.fuelType).toBe(defaultProps.fuelType);
        expect(voucher.fuelSubtype).toBe(defaultProps.fuelSubtype);
        expect(voucher.amount).toBe(defaultProps.amount);
        expect(voucher.unit).toBe(defaultProps.unit);
        expect(voucher.expirationDate).toBe(defaultProps.expirationDate);
        expect(voucher.redemptionRules).toBe(defaultProps.redemptionRules);
        expect(voucher.imageUrl).toBe(defaultProps.imageUrl);
        expect(voucher.qrCodeData).toBe(defaultProps.qrCodeData);
        expect(voucher.originalFileName).toBe(defaultProps.originalFileName);
        expect(voucher.source).toBe(defaultProps.source);
        expect(voucher.importJobId).toBe(defaultProps.importJobId);
        expect(voucher.purchaseId).toBe(defaultProps.purchaseId);
        expect(voucher.createdAt).toBe(defaultProps.createdAt);
        expect(voucher.updatedAt).toBe(defaultProps.updatedAt);
        expect(voucher.toPlainObject()).toEqual(defaultProps);
    });
});
