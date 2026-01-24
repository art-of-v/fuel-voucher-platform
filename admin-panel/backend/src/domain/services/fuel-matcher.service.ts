/**
 * Fuel Matcher Domain Service
 * 
 * Handles fuel type matching and alias resolution.
 * This is pure domain logic, moved out of the repository layer.
 */

/**
 * Fuel type alias mappings
 * Maps canonical fuel types to their aliases (including Cyrillic variants)
 */
const FUEL_ALIASES: Record<string, string[]> = {
    // Gasoline types
    'A-95': ['A95', 'А-95', 'А95', '95', 'Pulls 95', 'PULLS 95', 'Euro 95', 'Premium 95'],
    'A-92': ['A92', 'А-92', 'А92', '92', 'Regular 92'],
    'A-98': ['A98', 'А-98', 'А98', '98', 'Super 98', 'Premium 98'],
    'A-100': ['A100', 'А-100', 'А100', '100', 'Ultimate 100'],

    // Diesel types
    'Diesel': ['ДП', 'ДТ', 'Diesel Euro', 'Дизель', 'Diesel', 'Euro Diesel', 'Pulls Diesel', 'PULLS DIESEL'],
    'Diesel+': ['ДП+', 'ДТ+', 'Diesel Plus', 'Дизель+', 'Premium Diesel'],

    // Gas types
    'LPG': ['ГАЗ', 'Gas', 'Газ', 'Автогаз', 'Пропан'],

    // Special fuels
    'PULLS 95 Extra Power': ['Pulls 95 Extra', '95 Extra', 'Extra Power 95'],
};

export class FuelMatcherService {
    /**
     * Get all aliases for a given fuel type (including the type itself)
     */
    getAliases(fuelType: string): string[] {
        const normalized = this.normalizeFuelType(fuelType);

        // Check if this is a canonical type
        if (FUEL_ALIASES[normalized]) {
            return [normalized, ...FUEL_ALIASES[normalized]];
        }

        // Check if this is an alias, find the canonical type
        for (const [canonical, aliases] of Object.entries(FUEL_ALIASES)) {
            const normalizedAliases = aliases.map(a => this.normalizeFuelType(a));
            if (normalizedAliases.includes(normalized)) {
                return [canonical, ...aliases];
            }
        }

        // No match found, return just the original type
        return [fuelType];
    }

    /**
     * Check if two fuel types match (considering aliases)
     */
    matches(fuelType1: string, fuelType2: string): boolean {
        const aliases1 = this.getAliases(fuelType1).map(a => this.normalizeFuelType(a));
        const normalized2 = this.normalizeFuelType(fuelType2);
        return aliases1.includes(normalized2);
    }

    /**
     * Get the canonical fuel type for a given alias
     */
    getCanonical(fuelType: string): string {
        const normalized = this.normalizeFuelType(fuelType);

        // Check if this is already canonical
        if (FUEL_ALIASES[normalized]) {
            return normalized;
        }

        // Find the canonical type
        for (const [canonical, aliases] of Object.entries(FUEL_ALIASES)) {
            const normalizedAliases = aliases.map(a => this.normalizeFuelType(a));
            if (normalizedAliases.includes(normalized)) {
                return canonical;
            }
        }

        // No match found, return original
        return fuelType;
    }

    /**
     * Normalize a fuel type for comparison
     */
    private normalizeFuelType(fuelType: string): string {
        return fuelType
            .trim()
            .toLowerCase()
            .replace(/\s+/g, ' ')
            .replace(/-/g, '');
    }

    /**
     * Get all known canonical fuel types
     */
    getAllCanonicalTypes(): string[] {
        return Object.keys(FUEL_ALIASES);
    }

    /**
     * Check if a fuel type is known
     */
    isKnownType(fuelType: string): boolean {
        const aliases = this.getAliases(fuelType);
        return aliases.length > 1 || FUEL_ALIASES[fuelType] !== undefined;
    }
}

// Export singleton instance for convenience
export const fuelMatcherService = new FuelMatcherService();

// Export the getFuelAliases function for backward compatibility
export function getFuelAliases(fuelType: string): string[] {
    return fuelMatcherService.getAliases(fuelType);
}
