import { apiRequest } from '../../../core/api/apiClient';

/**
 * Requests a step-up-guarded self-service email change.
 *
 * The `code` is a fresh OTP that proves control of the CURRENT account — it was delivered by
 * `/api/auth/send-code` to the account's existing phone/email (never to the new address). On
 * success the backend only STAGES the new address and emails a confirmation link to it; the active
 * email changes when that link is opened, so callers should tell the user to check the new inbox,
 * not that the address already changed.
 *
 * A wrong/expired step-up code returns 403 (not 401) by design: `apiFetch` runs its
 * refresh-then-clear-tokens cascade only on a 401, so a 403 passes straight through here and the
 * user is NOT logged out for a mistyped code. We surface it as a retryable `INVALID_CODE`.
 */
export async function requestEmailChange(email: string, code: string): Promise<void> {
  const response = await apiRequest('POST', '/api/users/email/change', { email, code });
  if (response.ok) return;

  if (response.status === 403) {
    const err = new Error('INVALID_CODE') as Error & { code?: string; status?: number };
    err.code = 'INVALID_CODE';
    err.status = 403;
    throw err;
  }

  // 429 is the per-account OTP rate limit (empty body), not a transport failure — tag it so the
  // caller can show "too many attempts" instead of a generic error.
  if (response.status === 429) {
    const err = new Error('RATE_LIMITED') as Error & { status?: number };
    err.status = 429;
    throw err;
  }

  const error = await response.json().catch(() => ({}));
  throw new Error(error.message || error.title || `Error (${response.status})`);
}
