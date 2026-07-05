import { stationsRepository } from "../../stations/stations.repository";
import { fuelTypesRepository } from "../../stations/fuel-types.repository";
import { packagesRepository } from "../../stations/packages.repository";
import { logger } from "../../../infrastructure/logging/logger";
import { fuelMatcherService } from "../../../domain/services/fuel-matcher.service";

const log = logger.child({ component: 'AutoMasterData' });

export const autoMasterDataService = {
    /**
     * Ensures that the station, fuel type, and package exist in the master data.
     * If they don't, it creates them so the imported fuel can be purchased.
     * Returns the canonical fuel type object.
     */
    async ensureMasterData(providerName: string, fuelName: string, liters: number) {
        try {
            // Normalize names
            const cleanProvider = providerName.trim();
            const cleanFuel = fuelName.trim();

            log.info(`Checking master data for ${cleanProvider} - ${cleanFuel} (${liters}L)`);

            // 1. Find Station
            const allStations = await stationsRepository.getAllStations();
            const station = allStations.find(s =>
                s.name.toLowerCase().trim() === cleanProvider.toLowerCase() ||
                s.id.toLowerCase().trim() === cleanProvider.toLowerCase()
            );

            if (!station) {
                log.warn(`Station "${cleanProvider}" not found in database. Cannot auto-create fuel/packages without a valid station.`);
                return;
            }

            // 2. Find or Create Fuel Type
            const fuelTypes = await fuelTypesRepository.getFuelTypesByStation(station.id);

            // Try to find a match using aliases
            const incomingAliases = fuelMatcherService.getAliases(cleanFuel).map(a => a.toLowerCase().trim());
            log.info(`Aliases for "${cleanFuel}": ${JSON.stringify(incomingAliases)}`);

            let fuelType = fuelTypes.find(ft => {
                const existingName = ft.name.toLowerCase().trim();
                const matched = incomingAliases.includes(existingName) || cleanFuel.toLowerCase() === existingName;
                if (matched) log.info(`Matched incoming "${cleanFuel}" to existing fuel type: "${ft.name}"`);
                return matched;
            });

            // Special case: If we matched A-95 but there's a more specific "EURO" version, pick that
            if (fuelType && fuelType.name === 'A-95 Mustang' && !cleanFuel.toLowerCase().includes('mustang')) {
                const euroVersion = fuelTypes.find(ft => ft.name.toLowerCase().includes('euro') || ft.name.toLowerCase().includes('євро'));
                if (euroVersion) fuelType = euroVersion;
            }

            if (!fuelType) {
                log.info(`Creating missing fuel type: "${cleanFuel}" for station "${station.name}"`);

                // Try to infer prices from existing A-95 if this is a variant
                let basePrice = 55;
                let discountPrice = 52;

                const similarFuel = fuelTypes.find(ft =>
                    ft.name.toLowerCase().includes('95') ||
                    ft.name.toLowerCase().includes('dp') ||
                    ft.name.toLowerCase().includes('diesel')
                );

                if (similarFuel) {
                    basePrice = similarFuel.basePrice;
                    discountPrice = similarFuel.discountPrice;
                    log.info(`Inferred prices from similar fuel "${similarFuel.name}": ${basePrice}/${discountPrice}`);
                }

                // Generate a safe, unique ID
                const fuelId = `${station.id}-${cleanFuel.toLowerCase().replace(/[^a-z0-9]/g, '-')}`;

                fuelType = await fuelTypesRepository.createFuelType({
                    id: fuelId,
                    name: cleanFuel,
                    stationId: station.id,
                    basePrice,
                    discountPrice
                });
            }

            // 3. Find or Create Package
            const packages = await packagesRepository.getPackagesByStation(station.id);
            const pkg = packages.find(p => p.fuelTypeId === fuelType!.id && p.liters === liters);

            if (!pkg) {
                log.info(`Creating missing fuel package: ${cleanFuel} ${liters}L for station "${station.name}"`);
                const pkgId = `${fuelType!.id}-${liters}`;

                await packagesRepository.createPackage({
                    id: pkgId,
                    stationId: station.id,
                    fuelTypeId: fuelType!.id,
                    fuelName: fuelType!.name,
                    liters: liters,
                    price: fuelType!.discountPrice * liters,
                    originalPrice: fuelType!.basePrice * liters
                });
            }

            return fuelType;
        } catch (error: any) {
            log.error(`Auto-master-data synchronization failed: ${error.message}`);
            return null;
        }
    }
};
