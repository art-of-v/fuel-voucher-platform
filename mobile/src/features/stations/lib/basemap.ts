import Constants from 'expo-constants';

/**
 * Resolve the CARTO basemap key the price-radar map tiles are requested with.
 *
 * The key is public (EXPO_PUBLIC_*): it ships in the JS bundle and travels as ?key= on every
 * tile request, so it is not a secret. What matters is that every build path resolves it the
 * same way. An EAS cloud build reads it from the inlined env var; a raw Xcode Document Archive
 * never sees that env, so it must fall back to the copy committed in app.json
 * (expo.extra.cartoApiKey). This mirrors resolveApiBaseUrl() and the Sentry DSN resolver:
 * env var (per-build override) first, then the app.json baseline baked into every build.
 *
 * Returns null when no key is configured anywhere — a valid state, not an error: map.tsx then
 * renders no raster overlay and the native basemap shows through, instead of CARTO's
 * "API KEY REQUIRED" watermark. See planning #102.
 */
export function resolveCartoApiKey(): string | null {
  const fromEnv = process.env.EXPO_PUBLIC_CARTO_API_KEY;
  if (fromEnv) return fromEnv;

  const fromExtra = Constants.expoConfig?.extra?.cartoApiKey;
  if (typeof fromExtra === 'string' && fromExtra) return fromExtra;

  return null;
}
