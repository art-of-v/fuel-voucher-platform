import { apiFetch } from '../../../core/api/apiClient';
import type { Station, FuelType } from '../../../core/types/api';

export async function getStations(): Promise<Station[]> {
  const response = await apiFetch('/api/brands');
  if (!response.ok) throw new Error('Failed to fetch stations');
  return response.json();
}

export async function getFuelTypes(): Promise<FuelType[]> {
  const response = await apiFetch('/api/brands/fuel-types');
  if (!response.ok) throw new Error('Failed to fetch fuel types');
  return response.json();
}
