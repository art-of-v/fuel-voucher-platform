/**
 * The Google Maps key is an Android secret: it ships inside the APK's manifest, so it must never
 * live in the committed `app.json`. It is read from the environment instead —
 * `GOOGLE_MAPS_API_KEY` in `.env` locally, and an EAS environment variable for a cloud build.
 *
 * Without it, `react-native-maps` does not degrade gracefully on Android: the Google Maps SDK
 * throws `IllegalStateException: API key not found` from a background thread, which takes the
 * whole app down when the map screen opens. iOS has no such requirement.
 */
const base = require('./app.json');

module.exports = () => {
  const apiKey = process.env.GOOGLE_MAPS_API_KEY;
  const android = base.expo.android ?? {};
  const ios = base.expo.ios ?? {};

  // App Attest rejects a build whose entitlement does not match the provisioning profile.
  // EAS production sets this to "production"; local development builds can set "development".
  // Anything else falls back to production rather than emitting an invalid entitlement.
  const appAttestEnvironment =
    process.env.APP_ATTEST_ENVIRONMENT === 'development' ? 'development' : 'production';
  const googleCloudProjectNumber =
    process.env.EXPO_PUBLIC_GOOGLE_CLOUD_PROJECT_NUMBER ?? process.env.GOOGLE_CLOUD_PROJECT_NUMBER;

  return {
    ...base.expo,
    ios: {
      ...ios,
      entitlements: {
        ...ios.entitlements,
        'com.apple.developer.devicecheck.appattest-environment': appAttestEnvironment,
      },
    },
    android: {
      ...android,
      ...(apiKey ? { config: { ...android.config, googleMaps: { apiKey } } } : {}),
    },
    extra: {
      ...base.expo.extra,
      ...(googleCloudProjectNumber ? { googleCloudProjectNumber } : {}),
    },
  };
};
