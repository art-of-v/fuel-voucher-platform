import { apiFetch } from '../../../core/api/apiClient';
import type { Company } from '../../../core/types/api';

export async function getLegalProfile(): Promise<{ company: Company | null }> {
  const response = await apiFetch('/api/legal-entity/profile');
  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    throw new Error(
      errorData.error?.message || errorData.error || errorData.message || 'Failed to fetch legal profile',
    );
  }
  return response.json();
}

export async function updateLegalProfile(
  data: Partial<Company>,
): Promise<Company> {
  const response = await apiFetch('/api/legal-entity/profile', {
    method: 'POST',
    body: JSON.stringify(data),
  });
  if (!response.ok) throw new Error('Failed to update legal profile');
  return response.json();
}
