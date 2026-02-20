import { Station, FuelType } from './api';

export type { Station, FuelType };

export interface FuelPackage {
  id: string;
  liters: number;
  price: number;
  originalPrice: number;
  fuelTypeId: string;
  stationId: string;
}

export const STATIONS: Station[] = [
  { id: 'okko', name: 'OKKO', color: 'bg-green-600', logoText: 'OKKO' },
  { id: 'wog', name: 'WOG', color: 'bg-green-500', logoText: 'WOG' },
  { id: 'upg', name: 'UPG', color: 'bg-emerald-500', logoText: 'UPG' },
  { id: 'klo', name: 'KLO', color: 'bg-yellow-500', logoText: 'KLO' },
];

export const FUELS: FuelType[] = [
  { id: 'okko-95', name: 'A-95', stationId: 'okko', basePrice: 55, discountPrice: 52 },
  { id: 'okko-95-pulls', name: 'A-95 Pulls', stationId: 'okko', basePrice: 60, discountPrice: 57 },
  { id: 'okko-dp', name: 'Diesel', stationId: 'okko', basePrice: 52, discountPrice: 49 },

  { id: 'wog-95', name: 'A-95 Mustang', stationId: 'wog', basePrice: 56, discountPrice: 53 },
  { id: 'wog-dp', name: 'Diesel Mustang', stationId: 'wog', basePrice: 53, discountPrice: 50 },

  { id: 'upg-95', name: 'A-95', stationId: 'upg', basePrice: 54, discountPrice: 51 },
  { id: 'upg-100', name: 'UPG-100', stationId: 'upg', basePrice: 65, discountPrice: 60 },
];

export const PACKAGES: FuelPackage[] = [
  // OKKO 95
  { id: 'pkg-okko-95-10', liters: 10, price: 520, originalPrice: 550, fuelTypeId: 'okko-95', stationId: 'okko' },
  { id: 'pkg-okko-95-20', liters: 20, price: 1040, originalPrice: 1100, fuelTypeId: 'okko-95', stationId: 'okko' },
  { id: 'pkg-okko-95-50', liters: 50, price: 2600, originalPrice: 2750, fuelTypeId: 'okko-95', stationId: 'okko' },

  // WOG 95
  { id: 'pkg-wog-95-10', liters: 10, price: 530, originalPrice: 560, fuelTypeId: 'wog-95', stationId: 'wog' },
  { id: 'pkg-wog-95-20', liters: 20, price: 1060, originalPrice: 1120, fuelTypeId: 'wog-95', stationId: 'wog' },

  // Generic fallback generator for others (simplified for mockup)
];

export function getPackagesForFuel(fuelTypeId: string, stationId: string): FuelPackage[] {
  // Return hardcoded or generate on fly if missing
  const existing = PACKAGES.filter(p => p.fuelTypeId === fuelTypeId);
  if (existing.length > 0) return existing;

  const fuel = FUELS.find(f => f.id === fuelTypeId);
  if (!fuel) return [];

  return [10, 20, 50].map(liters => ({
    id: `gen-${fuelTypeId}-${liters}`,
    liters,
    price: fuel.discountPrice * liters,
    originalPrice: fuel.basePrice * liters,
    fuelTypeId,
    stationId
  }));
}
