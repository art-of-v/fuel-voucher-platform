import { apiFetch } from '../../../core/api/apiClient';
import type { Company } from '../../../core/types/api';
import { LegalEntityApiError } from '../../company/api/legalEntityApi';

// GET /api/legal-entity/profile returns the profile object directly (not
// wrapped) and 404 when the user has no legal entity yet. Returns null in that
// case so callers can treat "not an owner" as a normal state.
export async function getLegalProfile(): Promise<Company | null> {
  const response = await apiFetch('/api/legal-entity/profile');
  if (response.status === 404) return null;
  if (!response.ok) {
    if (response.status === 401) return null;
    const errorData = await response.json().catch(() => ({}));
    throw new Error(errorData.detail || errorData.title || 'Failed to fetch legal profile');
  }
  return response.json();
}

export async function updateLegalProfile(data: Partial<Company>): Promise<Company> {
  const response = await apiFetch('/api/legal-entity/profile', {
    method: 'POST',
    body: JSON.stringify(data),
  });
  if (!response.ok) {
    // The status used to be thrown away, which cost two things: the caller could not tell a
    // duplicate EDRPOU (409) from a rejected format (400), so every failure became the same
    // "Error" toast, and `err.status ?? 500` read the missing status as a server fault and
    // shipped the user's typo to Sentry as an incident. Same error type as the scoped
    // PUT /api/legal-entity/{id} path so one mapper handles both.
    const body = await response.json().catch(() => ({}) as any);
    const message = body.detail || body.title || 'Failed to update legal profile';
    throw new LegalEntityApiError(response.status, message);
  }
  return response.json();
}
