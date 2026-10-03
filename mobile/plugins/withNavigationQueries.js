const { withAndroidManifest } = require('expo/config-plugins');

/**
 * The Android counterpart of the iOS `LSApplicationQueriesSchemes` entries in app.json.
 *
 * Android 11+ hides other apps from `Linking.canOpenURL` unless they are declared in the
 * manifest's `<queries>` element. Without this the navigator probe reports every third-party maps
 * app as "not installed", and the "build route" picker would offer nothing but browser fallbacks.
 * Keep this list in sync with `LSApplicationQueriesSchemes` in app.json.
 */
const SCHEMES = ['waze', 'com.google.android.apps.maps', 'com.yandex.yandexmaps', 'here-wego', 'osmand', 'osmandplus', 'org.organicmaps'];

const intentForScheme = (scheme) => ({
  intent: [
    { $: { 'android:name': 'android.intent.action.VIEW' } },
    { $: { 'android:scheme': scheme } },
  ],
});

module.exports = function withNavigationQueries(config) {
  return withAndroidManifest(config, (config) => {
    const manifest = config.modResults.manifest;
    manifest.$ = manifest.$ || {};

    const declared = manifest.$['queries'] || [];
    const existing = declared.flatMap((query) =>
      (query.intent || []).map((entry) => entry?.$?.['android:scheme']).filter(Boolean),
    );
    manifest.$['queries'] = [...declared, ...SCHEMES.filter((scheme) => !existing.includes(scheme)).map(intentForScheme)];

    // `<queries>` is validated by AAPT before `<application>`, so it cannot simply be appended.
    const application = manifest.find((element) => element.application);
    const applicationIndex = application ? manifest.indexOf(application) : manifest.length;
    manifest.splice(applicationIndex, 0, { queries: manifest.$['queries'] });
    delete manifest.$['queries'];

    return config;
  });
};