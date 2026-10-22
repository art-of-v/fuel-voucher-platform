import { Platform } from 'react-native';
import Constants from 'expo-constants';
import { BASE_URL } from '../api/apiConfig';
import { ensureSslPinningInitialized } from '../security/sslPinning';

export interface AppVersionInfo {
  minimumVersion: string;
  iosStoreUrl: string;
  androidStoreUrl: string;
}

export function parseVersion(version: string): number[] {
  return version.split('.').map(Number);
}

/**
 * Whether `current` is genuinely older than `minimum`.
 *
 * `current` is null when the build could not read its own version — `Constants.expoConfig` is not
 * reliably populated in a standalone App Store binary. That case returns FALSE on purpose.
 *
 * The previous fallback returned the literal '1.0.0', which was *below* the floor the API served at
 * the time (1.0.1). So a build that could not read its own version was declared older than the minimum and
 * shown a full-screen update wall, and the wall offered a store build that would fail the same check —
 * the same brick the floor's own comment warns about, reached from the other side. Blocking on a value
 * that was never read is not caution, it is refusing to serve.
 */
export function isVersionBelow(current: string | null, minimum: string): boolean {
  if (!current) return false;

  const currentParts = parseVersion(current);
  const minimumParts = parseVersion(minimum);
  const maxLen = Math.max(currentParts.length, minimumParts.length);
  for (let i = 0; i < maxLen; i++) {
    const a = currentParts[i] ?? 0;
    const b = minimumParts[i] ?? 0;
    if (a !== b) return a < b;
  }
  return false;
}

export async function fetchAppVersion(): Promise<AppVersionInfo | null> {
  try {
    // This is one of the first requests at launch and it bypasses apiFetch, so initialize
    // pinning before letting it out.
    await ensureSslPinningInitialized(BASE_URL);

    const response = await fetch(`${BASE_URL}/api/app-version`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (!response.ok) return null;
    return await response.json();
  } catch {
    return null;
  }
}

export function getStoreUrl(info: AppVersionInfo): string {
  if (Platform.OS === 'ios') return info.iosStoreUrl;
  if (Platform.OS === 'android') return info.androidStoreUrl;
  return '';
}

/**
 * This build's version, or null when it cannot be determined.
 *
 * Null rather than a guessed literal. `expoConfig` is absent in some production builds, and any
 * substitute we invent is a version we did not verify — and if the guess happens to be below the
 * server's floor, the app walls itself off for a build that is current. Callers must handle null by
 * NOT blocking; see {@link isVersionBelow}.
 */
export function getCurrentAppVersion(): string | null {
  return Constants.expoConfig?.version ?? null;
}
