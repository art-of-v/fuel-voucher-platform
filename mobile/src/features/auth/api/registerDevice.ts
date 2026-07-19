import { apiRequest } from '../../../core/api/apiClient';

export async function registerDevice(
  deviceId: string,
  publicKey: string,
  metadata: Record<string, string>,
  accessToken: string,
): Promise<void> {
  const response = await apiRequest(
    'POST',
    '/api/auth/device/register',
    { deviceId, publicKey, ...metadata },
    { Authorization: `Bearer ${accessToken}` },
  );
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error?.message || 'Помилка реєстрації пристрою');
  }
}

export async function getChallenge(deviceId: string): Promise<string> {
  const response = await apiRequest('POST', '/api/auth/device/challenge', { deviceId });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error?.message || 'Помилка отримання challenge');
  }
  const data = await response.json();
  return data.challenge;
}

export async function verifyChallenge(
  deviceId: string,
  challenge: string,
  signature: string,
): Promise<{ accessToken: string; refreshToken: string }> {
  const response = await apiRequest('POST', '/api/auth/device/verify', {
    deviceId,
    challenge,
    signature,
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error?.message || 'Помилка верифікації пристрою');
  }
  return response.json();
}
