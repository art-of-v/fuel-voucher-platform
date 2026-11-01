/**
 * The slice of app.json's expo config that the installed app reads through
 * `Constants.expoConfig` after an OTA update.
 *
 * The manifest is served to anyone - the endpoint is anonymous and the bundle sits beside it -
 * so carrying the whole app config made every published build a config mirror: bundle ids, the
 * native build number, plugins, icon paths, the signing key id and the updates URL. None of it is
 * a secret (app.json is in this repository), and none of it is read either. This module keeps the
 * fields the JS actually looks at and drops the rest.
 *
 * The list below is not a guess: `ota-client-config.test.mjs` scans `app/` and `src/` for
 * `Constants.expoConfig?.extra.<key>` reads and fails when one appears that is not here - so a
 * new read either joins the manifest or breaks the build, instead of silently returning undefined
 * on a device whose JS arrived over the air.
 */
export const CLIENT_EXTRA_KEYS = Object.freeze([
  'apiUrl',
  'eas',
  'sentryDsn',
  'cartoApiKey',
  'googleCloudProjectNumber',
]);

/** Top-level expo config fields the app reads; `extra` is handled separately. */
export const CLIENT_TOP_LEVEL_KEYS = Object.freeze(['version', 'scheme']);

/** `eas` is read only for its project id (push registration); the rest of it is build metadata. */
function narrowExtra(key, value) {
  if (key !== 'eas' || value === null || typeof value !== 'object') {
    return value;
  }

  return { projectId: value.projectId };
}

/**
 * Build the `extra.expoClient` payload for a signed manifest from app.json's expo block.
 *
 * <p>
 * Absent keys stay absent rather than becoming `null`: `Constants.expoConfig?.extra?.apiUrl`
 * already handles undefined, and an explicit null is a value the app does not expect. Unknown
 * extra keys are dropped on purpose - this is an allowlist, not a copy.
 * </p>
 */
export function clientConfigForManifest(appConfig = {}) {
  const configuredExtra = appConfig.extra ?? {};
  const extra = {};

  for (const key of CLIENT_EXTRA_KEYS) {
    if (configuredExtra[key] !== undefined) {
      extra[key] = narrowExtra(key, configuredExtra[key]);
    }
  }

  const client = {};

  for (const key of CLIENT_TOP_LEVEL_KEYS) {
    if (appConfig[key] !== undefined) {
      client[key] = appConfig[key];
    }
  }

  client.extra = extra;
  return client;
}
