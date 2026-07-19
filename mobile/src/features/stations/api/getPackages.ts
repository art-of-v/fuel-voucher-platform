import { apiFetch } from '../../../core/api/apiClient';
import type { FuelPackage } from '../../../core/types/api';

export async function getPackages(): Promise<FuelPackage[]> {
  const response = await apiFetch('/api/packages');
  if (!response.ok) throw new Error('Failed to fetch packages');
  return response.json();
}
