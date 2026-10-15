const { withAndroidManifest } = require('expo/config-plugins');

/**
 * The Android counterpart of the iOS `LSApplicationQueriesSchemes` entries in app.json.
 *
 * Android 11+ hides other apps from `Linking.canOpenURL` unless they are declared in the
 * manifest's `<queries>` element. Without this the navigator probe reports every third-party maps
 * app as "not installed", and the "build route" picker would offer nothing but browser fallbacks.
 * Keep this list in sync with `LSApplicationQueriesSchemes` in app.json.
 */
const SCHEMES = ['waze', 'com.google.android.apps.maps', 'here-wego', 'osmand', 'osmandplus', 'org.organicmaps'];

const intentForScheme = (scheme) => ({
  action: [{ $: { 'android:name': 'android.intent.action.VIEW' } }],
  data: [{ $: { 'android:scheme': scheme } }],
});

/**
 * `modResults.manifest` is a plain object (Expo's xml2js post-processing unwraps the root
 * element), so every child is a keyed collection — `manifest.queries`, never an array to
 * `find`/`splice` into. Manifest entries are also not one-per-array-slot: the single `<queries>`
 * element xml2js hands back as `queries[0]` holds every `<intent>` in its own `intent` array,
 * because that is the only shape the builder serialises back into one `<queries>` — spreading the
 * schemes over `queries` instead emits a repeated `<queries>` element per scheme, which AAPT
 * rejects as a duplicate.
 */
module.exports = function withNavigationQueries(config) {
  return withAndroidManifest(config, (config) => {
    const manifest = config.modResults.manifest;
    const queries = Array.isArray(manifest.queries) ? manifest.queries : [];

    const declaredSchemes = queries.flatMap((query) =>
      (query.intent || [])
        .flatMap((intent) => (intent.data || []).map((datum) => datum?.$?.['android:scheme']))
        .filter(Boolean),
    );

    const added = SCHEMES.filter((scheme) => !declaredSchemes.includes(scheme)).map(intentForScheme);
    if (added.length === 0) return config;

    const [host] = queries;
    if (host) {
      host.intent = [...(host.intent || []), ...added];
    } else {
      queries.push({ intent: added });
    }

    if (Array.isArray(manifest.queries)) {
      manifest.queries = queries;
    } else {
      // AAPT validates `<queries>` ahead of `<application>`, so when the generated manifest has no
      // `<queries>` at all the key has to be inserted rather than appended to the end.
      const reordered = {};
      for (const [key, value] of Object.entries(manifest)) {
        if (key === 'application') reordered.queries = queries;
        reordered[key] = value;
      }
      if (!('queries' in reordered)) reordered.queries = queries;
      for (const key of Object.keys(manifest)) delete manifest[key];
      Object.assign(manifest, reordered);
    }

    return config;
  });
};