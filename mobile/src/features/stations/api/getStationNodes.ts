import { apiFetch } from '../../../core/api/apiClient';
import type { StationNode } from '../../../core/types/api';

export async function getStationNodes(): Promise<StationNode[]> {
  const response = await apiFetch('/api/station-nodes');
  if (!response.ok) throw new Error('Failed to fetch station nodes');
  return response.json();
}
