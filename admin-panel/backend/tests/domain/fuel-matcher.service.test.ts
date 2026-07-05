
import { describe, it, expect } from 'vitest';
import { fuelMatcherService, getFuelAliases } from '../../src/domain/services/fuel-matcher.service';

describe('Fuel Matcher Service', () => {
    it('should find aliases for known fuel type', () => {
        const aliases = fuelMatcherService.getAliases('A-95');
        expect(aliases).toContain('A-95'); // Canonical
        expect(aliases).toContain('A95');  // Alias
        expect(aliases).toContain('А-95'); // Cyrillic
    });

    it('should find aliases starting from an alias', () => {
        const aliases = fuelMatcherService.getAliases('А95'); // Cyrillic/No dash
        expect(aliases).toContain('A-95'); // Should find canonical group
        expect(aliases).toContain('Pulls 95');
    });

    it('should return self if unknown', () => {
        const aliases = fuelMatcherService.getAliases('RocketFuel');
        expect(aliases).toEqual(['RocketFuel']);
    });

    it('should match known types correctly', () => {
        expect(fuelMatcherService.matches('A-95', 'A95')).toBe(true);
        expect(fuelMatcherService.matches('A-95', 'А-95')).toBe(true); // Canonical vs Alias
        expect(fuelMatcherService.matches('A95', 'Pulls 95')).toBe(true); // Alias vs Alias
        expect(fuelMatcherService.matches('Diesel', 'ДП')).toBe(true);
    });

    it('should normalize inputs during matching (case/space)', () => {
        expect(fuelMatcherService.matches('a-95 ', 'A95')).toBe(true);
        expect(fuelMatcherService.matches('diesel', 'DIESEL')).toBe(true);
    });

    it('should not match different types', () => {
        expect(fuelMatcherService.matches('A-95', 'A-92')).toBe(false);
        expect(fuelMatcherService.matches('Diesel', 'LPG')).toBe(false);
    });

    it('should get canonical type', () => {
        expect(fuelMatcherService.getCanonical('A95')).toBe('A-95');
        expect(fuelMatcherService.getCanonical('ДП')).toBe('Diesel');
        expect(fuelMatcherService.getCanonical('A-95')).toBe('A-95');
        expect(fuelMatcherService.getCanonical('Unknown')).toBe('Unknown');
    });

    it('should list all canonical types', () => {
        const types = fuelMatcherService.getAllCanonicalTypes();
        expect(types).toContain('A-95');
        expect(types).toContain('Diesel');
        expect(types).toContain('LPG');
    });

    it('should check if known type', () => {
        expect(fuelMatcherService.isKnownType('A-95')).toBe(true);
        expect(fuelMatcherService.isKnownType('ДП')).toBe(true); // Alias is known
        expect(fuelMatcherService.isKnownType('UnknownFuel')).toBe(false);
    });

    it('should export legacy helper function', () => {
        expect(getFuelAliases('A-95')).toEqual(fuelMatcherService.getAliases('A-95'));
    });
});
