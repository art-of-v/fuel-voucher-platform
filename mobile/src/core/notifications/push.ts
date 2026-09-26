import { Platform } from 'react-native';
import Constants from 'expo-constants';
import * as Notifications from 'expo-notifications';

import { registerPushToken } from '../../features/notifications/api/pushTokensApi';

/**
 * Requests notification permission, mints this device's Expo push token, and
 * hands it to the backend. Called post-auth from the root layout (AuthSync).
 *
 * Best-effort by contract: every failure path — permission denied, no EAS
 * projectId, a simulator that cannot mint an APNs token, or the backend being
 * unreachable — resolves to `null` and never throws. A push problem must never
 * block login or crash the app.
 *
 * De-duped in-flight: AuthSync's effect can fire more than once as auth state
 * settles, so concurrent calls share one promise rather than prompting or
 * hitting the network repeatedly. It resets once settled, so a later session
 * (or a retry after failure) registers again.
 *
 * VERIFIABILITY: expo-notifications links a NATIVE module and needs the
 * `aps-environment` entitlement (app.json) plus an APNs key configured on the
 * Expo project. This JS wiring builds and is unit-tested, but whether a real
 * token is minted and a push actually arrives can only be confirmed in an EAS
 * build on a physical device — a simulator cannot mint an APNs token and Jest
 * cannot load the native module. A native rebuild + App Store resubmit is
 * required after this ships; it is not OTA-able.
 */
let inFlight: Promise<string | null> | null = null;

export function registerForPushNotifications(): Promise<string | null> {
  if (inFlight) return inFlight;
  inFlight = doRegister().finally(() => {
    inFlight = null;
  });
  return inFlight;
}

function resolveProjectId(): string | undefined {
  const fromExtra = Constants.expoConfig?.extra?.eas?.projectId;
  return typeof fromExtra === 'string' && fromExtra ? fromExtra : undefined;
}

async function doRegister(): Promise<string | null> {
  try {
    const existing = await Notifications.getPermissionsAsync();
    let granted = existing.status === 'granted';
    if (!granted) {
      const requested = await Notifications.requestPermissionsAsync();
      granted = requested.status === 'granted';
    }
    if (!granted) return null;

    const projectId = resolveProjectId();
    if (!projectId) return null;

    const { data: token } = await Notifications.getExpoPushTokenAsync({ projectId });
    if (!token) return null;

    await registerPushToken(token, Platform.OS);
    return token;
  } catch {
    // Permission API, native token minting (throws on a simulator), or the
    // backend call failed. Best-effort: swallow and let a later login retry.
    return null;
  }
}
