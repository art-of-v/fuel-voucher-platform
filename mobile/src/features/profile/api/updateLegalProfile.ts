import { apiFetch } from '../../../core/api/apiClient';
import type { Company } from '../../../core/types/api';

// GET /api/legal-entity/profile returns the profile object directly (not
// wrapped) and 404 when the user has no legal entity yet. Returns null in that
// case so callers can treat "not an owner" as a normal state.
export async function getLegalProfile(): Promise<Company | null> {
  const response = await apiFetch('/api/legal-entity/profile');
  if (response.status === 404) return null;
  if (!response.ok) {
    if (response.status === 401) return null;
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
