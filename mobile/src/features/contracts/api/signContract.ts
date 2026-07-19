import { apiFetch } from '../../../core/api/apiClient';
import type { UserContract } from '../../../core/types/api';

export async function signContracts(
  contractIds: string[],
  signatureData: string,
  stationId?: string,
): Promise<UserContract[]> {
  const response = await apiFetch('/api/legal-entity/sign', {
    method: 'POST',
    body: JSON.stringify({ contractIds, signatureData, stationId }),
    headers: {
      'x-force-signature': 'true',
    },
  });
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    throw new Error(
      errorData.error?.message || errorData.error || errorData.message || 'Failed to sign contracts',
    );
  }
  return response.json();
}
