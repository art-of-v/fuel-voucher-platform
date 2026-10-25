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
    // The status travels with the error: a refused code (401) is the person's mistake and gets
    // its own message, a server failure is ours and must not be shown as "wrong code".
    const err = new Error(error.detail || error.title || 'Invalid verification code') as Error & {
      status?: number;
    };
    err.status = response.status;
    throw err;
  }
  return response.json();
}
