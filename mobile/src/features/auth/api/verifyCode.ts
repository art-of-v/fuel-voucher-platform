import { apiRequest } from '../../../core/api/apiClient';
import type { AuthResponse } from '../types';

export async function verifyPhoneCode(phoneNumber: string, code: string): Promise<AuthResponse> {
  const response = await apiRequest('POST', '/api/auth/verify', {
    phoneNumber,
    code,
  });
  if (!response.ok) {
    // 429 = verify rate limit (5 / 5 min per phone). Tag it so the caller can show a
    // "too many attempts" message rather than "wrong code" / a generic network error.
    if (response.status === 429) {
      const err = new Error('RATE_LIMITED') as Error & { status?: number };
      err.status = 429;
      throw err;
    }
    const error = await response.json().catch(() => ({}));
    // The status travels with the error: a refused code (401) is the person's mistake and
    // gets its own message, a server failure is ours and must not be shown as "wrong code".
    const err = new Error(error.detail || error.title || 'Invalid verification code') as Error & {
      status?: number;
    };
    err.status = response.status;
    throw err;
  }
  return response.json();
}
