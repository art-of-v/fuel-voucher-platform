import { apiRequest } from '../../../core/api/apiClient';
import type { AuthResponse } from '../types';

/**
 * The second factor of a staff sign-in: the code from the authenticator app redeems the
 * challenge and mints the session.
 *
 * <p>
 * Only a staff account needs this. A customer never sees it, so the app asks for the code
 * only when the sign-in answers with a challenge.
 * </p>
 */
export async function verifyTwoFactor(challenge: string, code: string): Promise<AuthResponse> {
  const response = await apiRequest('POST', '/api/auth/two-factor/verify', {
    challenge,
    code,
  });
  if (!response.ok) {
    // 429 = verify rate limit. Same tagging as the first factor, so the caller can show the
    // "too many attempts" message rather than "wrong code".
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
