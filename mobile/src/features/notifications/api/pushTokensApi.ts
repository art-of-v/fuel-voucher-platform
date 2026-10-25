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

/**
 * Best-effort park of THIS device's push token on logout. The backend matches on the
 * x-device-id header apiRequest always attaches (the same id slice 1 stored on the row), so
 * no token argument is needed and none can be cheaply re-minted here. MUST run before
 * SecurityService.revokeSecurity() clears the device id, or the server has nothing to match.
 * Any failure is swallowed — a failed park must never block logout.
 *
 * Parking is not deregistering. The device stops receiving the platform's business
 * notifications, but it stays addressable for the SIGN-IN CODE: a device that signed out is
 * exactly where the worker signs back in from, and a code that cannot reach it falls back to
 * the paid SMS channel — the channel the push channel exists to avoid. The old build (236)
 * on the fleet still calls DELETE /api/notifications/push-tokens on logout; the backend parks
 * on that route too, so the fleet does not need a new bundle to be fixed.
 */
export async function parkPushToken(): Promise<void> {
  try {
    await apiRequest('POST', '/api/notifications/push-tokens/park');
  } catch {
    // best-effort: never block logout on a failed park
  }
}
