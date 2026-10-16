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

  return {
    ...base.expo,
    android: {
      ...android,
      ...(apiKey ? { config: { ...android.config, googleMaps: { apiKey } } } : {}),
    },
  };
};