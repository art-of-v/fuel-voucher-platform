import { apiFetch } from '../../../core/api/apiClient';
import type { Company } from '../../../core/types/api';

// Multi-company endpoints (epic #103, S0 backend). Unlike the legacy
// `/api/legal-entity/profile` (which upserts a single entity), these list and
// create across the 0..N legal entities a user may now own.

export interface CreateLegalEntityInput {
  name: string;
  edrpou: string;
  vatNumber?: string;
  directorName?: string;
  address?: string;
  phone?: string;
  email?: string;
}

/** Error carrying the HTTP status so callers can map 409 (dup EDRPOU) to copy. */
export class LegalEntityApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message || `HTTP_${status}`);
    this.name = 'LegalEntityApiError';
    this.status = status;
  }
}

// GET /api/legal-entity/mine returns the user's legal entities as a bare array,
// oldest-first. 401 (not signed in) degrades to an empty list rather than an
// error so the contexts screen can render the personal root alone.
export async function getMyLegalEntities(): Promise<Company[]> {
  const response = await apiFetch('/api/legal-entity/mine');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw new LegalEntityApiError(response.status, 'Failed to load legal entities');
  }
  const data = await response.json();
  return Array.isArray(data) ? data : [];
}

// POST /api/legal-entity creates a new legal entity and returns it. The backend
// answers 409 when the EDRPOU is already registered (unique across users).
export async function createLegalEntity(
  data: CreateLegalEntityInput,
): Promise<Company> {
  const response = await apiFetch('/api/legal-entity', {
    method: 'POST',
    body: JSON.stringify(data),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => ({} as any));
    const message =
      body.error?.message || body.error || body.message || 'Failed to create legal entity';
    throw new LegalEntityApiError(response.status, message);
  }
  return response.json();
}
