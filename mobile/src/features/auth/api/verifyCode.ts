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
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || 'Невірний код');
  }
  return response.json();
}
