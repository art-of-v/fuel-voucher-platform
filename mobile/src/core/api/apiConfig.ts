import { Platform } from 'react-native';
import Constants from 'expo-constants';

// Single committed production URL — mirrored in app.json (expo.extra.apiUrl) and the
// eas.json build profiles, kept identical by scripts/check-api-config.mjs.
export const PRODUCTION_API_URL = 'https://api.palne.shop';

export const CLIENT_PLATFORM_HEADER = 'X-Client-Platform';
export const NATIVE_CLIENT_PLATFORM = 'mobile';

function resolveApiBaseUrl(): string {
  // 1. Build-time env — EAS build profiles (eas.json) or a local .env. Present in most builds.
  const fromEnv = process.env.EXPO_PUBLIC_API_URL;
  if (fromEnv) return fromEnv;

  // 2. Embedded app config. expo.extra.apiUrl is compiled into every build path, including a
  //    raw Xcode Archive that never saw the shell env at bundle time.
  const fromExtra = Constants.expoConfig?.extra?.apiUrl;
  if (typeof fromExtra === 'string' && fromExtra) return fromExtra;

  // 3. Local dev servers only — unreachable from any store/TestFlight build.
  if (__DEV__) {
    return Platform.OS === 'android' ? 'http://10.0.2.2:5000' : 'http://localhost:5000';
  }

  // 4. Last resort: the known production backend. This function runs at module load (see
  //    BASE_URL below), before React mounts, so it must NEVER throw — a throw here is an
  //    uncatchable native SIGABRT at launch, not something ErrorBoundary can surface.
  //    PRODUCTION_API_URL is the same value as steps 1–2, so this is a fallback, not a guess.
  return PRODUCTION_API_URL;
}

export const BASE_URL = resolveApiBaseUrl();
