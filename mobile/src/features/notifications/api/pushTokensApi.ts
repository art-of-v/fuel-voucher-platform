import { apiRequest } from '../../../core/api/apiClient';

/**
 * Registers this device's Expo push token with the backend so the server can
 * target it (slice 2 wires the actual send). Authenticated: apiRequest attaches
 * the stored bearer, so this must run post-login.
 *
 * Throws on a non-2xx response, but the only caller (registerForPushNotifications)
 * treats it as best-effort and swallows the throw — a failed registration must
 * never surface to the user or block login.
 */
export async function registerPushToken(token: string, platform: string): Promise<void> {
  const response = await apiRequest('POST', '/api/notifications/push-tokens', { token, platform });
  if (!response.ok) {
    throw new Error('Failed to register push token');
  }
}
