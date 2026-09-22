import { apiRequest } from '../../../core/api/apiClient';

export async function sendVerificationCode(phoneNumber: string): Promise<void> {
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
    throw new Error(error.message || error.title || 'ПОМИЛКА МЕРЕЖІ');
  }
}
