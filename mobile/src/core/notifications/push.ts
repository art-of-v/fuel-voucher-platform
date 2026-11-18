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

/**
 * Asks for the notification permission, if it has never been decided yet, and reports whether
 * this device can display a push right now.
 *
 * Sign-in needs this BEFORE the code is requested, not after it. The first code a person ever
 * asks for is delivered by push to the account's own devices, and iOS keeps that push off a
 * screen it was never granted: a freshly installed app whose permission prompt only appears
 * *after* a successful sign-in has no way to receive the very code that would get it there.
 * Registration still happens post-auth (the backend binds a token to a user), but the
 * permission has to be decided by then.
 *
 * Never throws and never blocks: a denied permission is an answer, not a failure.
 */
export async function ensurePushPermission(): Promise<boolean> {
  try {
    const existing = await Notifications.getPermissionsAsync();
    if (existing.status === 'granted') return true;
    // `undetermined` is the only state a prompt can change. `denied` is final for this install
    // (iOS never asks twice), so asking again would only flash a no-op.
    if (existing.status !== 'undetermined') return false;
    const requested = await Notifications.requestPermissionsAsync();
    return requested.status === 'granted';
  } catch {
    return false;
  }
}

async function doRegister(): Promise<string | null> {
  try {
    if (!(await ensurePushPermission())) return null;

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
