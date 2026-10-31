import { apiRequest } from '../../../core/api/apiClient';

/** What a successful send tells the caller beyond "it went out". */
export interface SendCodeResult {
  /**
   * How long the code stays valid, in seconds, as published by the server. The code step
   * needs it to know when the digits it is showing have died; it is a public constant for
   * every account, not an account-specific value.
   */
  expiresInSeconds?: number;
}

export async function sendVerificationCode(phoneNumber: string): Promise<SendCodeResult> {
  const response = await apiRequest('POST', '/api/auth/send-code', { phoneNumber });
  if (!response.ok) {
    // A 429 is the per-phone / per-IP OTP rate limit, not a transport failure. Tag it so
    // the caller can show "too many attempts, wait a minute" instead of a generic network
    // error (the limiter returns an empty body, so there is no message to fall back to).
    if (response.status === 429) {
      const err = new Error('RATE_LIMITED') as Error & { status?: number };
      err.status = 429;
      throw err;
    }
    const error = await response.json().catch(() => ({}));
    throw new Error(error.detail || error.title || 'ПОМИЛКА МЕРЕЖІ');
  }

  // A body that cannot be read must not fail the sign-in: the code has been sent either way,
  // and a client that predates the deadline just shows no countdown.
  const body = await response.json().catch(() => null);
  const expiresInSeconds =
    typeof body?.expiresInSeconds === 'number' && body.expiresInSeconds > 0
      ? body.expiresInSeconds
      : undefined;

  return { expiresInSeconds };
}
