import { apiFetch } from '../../../core/api/apiClient';
import type { Contract, UserContract } from '../../../core/types/api';

export async function getAvailableContracts(): Promise<Contract[]> {
  const response = await apiFetch('/api/legal-entity/contracts');
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    throw new Error(
      errorData.error?.message || errorData.error || errorData.message || 'Failed to fetch contracts',
    );
  }
  return response.json();
}

export async function getSignedContracts(): Promise<UserContract[]> {
  const response = await apiFetch('/api/legal-entity/contracts/signed');
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    throw new Error(
      errorData.error?.message || errorData.error || errorData.message || 'Failed to fetch signed contracts',
    );
  }
  return response.json();
}
