import type { Station, FuelType } from '../../../core/types/api';

export interface StationWithFuels extends Station {
  fuels: FuelType[];
}

export interface FuelCategory {
  id: string;
  name: string;
  stationId: string;
  basePrice: number;
  discountPrice: number;
}

export const STATION_PRIORITY_ORDER = ['okko', 'wog', 'upg', 'klo'];
