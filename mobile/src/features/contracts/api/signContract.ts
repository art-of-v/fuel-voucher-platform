import { apiFetch } from '../../../core/api/apiClient';
import type { ContractSignature } from '../../../core/types/api';

/**
 * Sign ONE contract.
 *
 * The body is `{ contractId, signatureData }` - the shape the server binds, and the only one
 * it has ever bound. The client used to post `{ contractIds: [...], signatureData, stationId }`:
 * the server read `ContractId`, found `Guid.Empty`, and answered 400 to every signature ever
 * offered. The screen signs one document at a time, so one contract per call is the whole
 * contract; the brand is already on the contract row and does not travel with the signature.
 */
export async function signContract(
  contractId: string,
  signatureData: string,
): Promise<ContractSignature> {
  const response = await apiFetch('/api/legal-entity/sign', {
    method: 'POST',
    body: JSON.stringify({ contractId, signatureData }),
    headers: {
      'x-force-signature': 'true',
    },
  });
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    throw new Error(errorData.detail || errorData.title || 'Failed to sign contract');
  }
  return response.json();
}
