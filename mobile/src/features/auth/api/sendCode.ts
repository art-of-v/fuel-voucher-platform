import { apiRequest } from '../../../core/api/apiClient';

export async function sendVerificationCode(phoneNumber: string): Promise<void> {
  const response = await apiRequest('POST', '/api/auth/send-code', { phoneNumber });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || error.title || 'ПОМИЛКА МЕРЕЖІ');
  }
}
