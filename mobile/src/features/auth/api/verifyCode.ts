import { apiRequest } from '../../../core/api/apiClient';
import type { AuthResponse } from '../types';

export async function verifyPhoneCode(
  phoneNumber: string,
  code: string,
): Promise<AuthResponse> {
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
    throw new Error(error.message || 'Невірний код');
  }
  return response.json();
}
