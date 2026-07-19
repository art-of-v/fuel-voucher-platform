import type { FuelPackage, Station, FuelType } from '../../../core/types/api';

export interface CartItem {
  id: string;
  package: FuelPackage;
  station: Station;
  fuel: FuelType;
  quantity: number;
}

export const PROMO_CODES: Record<string, number> = {
  FUEL10: 10,
  SAVE15: 15,
  POWER20: 20,
  LEMBERG25: 25,
};
