import { apiRequest } from '../../../core/api/apiClient';

export async function registerDevice(
  deviceId: string,
  publicKey: string,
  metadata: Record<string, string>,
  accessToken: string,
  registrationNonce?: string,
): Promise<void> {
  const response = await apiRequest(
    'POST',
    '/api/auth/device/register',
    { deviceId, publicKey, registrationNonce, ...metadata },
    { Authorization: `Bearer ${accessToken}` },
  );
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.detail || error.title || 'Не вдалося зареєструвати пристрій');
  }
}

export async function getChallenge(deviceId: string, accessToken?: string): Promise<string> {
  const response = await apiRequest(
    'POST',
    '/api/auth/device/challenge',
    { deviceId },
    accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
  );
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.detail || error.title || 'Помилка отримання challenge');
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
    throw new Error(error.detail || error.title || 'Помилка верифікації пристрою');
  }
  return response.json();
}
