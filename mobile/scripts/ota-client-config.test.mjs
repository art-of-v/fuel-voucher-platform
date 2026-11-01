import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  CLIENT_EXTRA_KEYS,
  CLIENT_TOP_LEVEL_KEYS,
  clientConfigForManifest,
} from './ota-client-config.mjs';

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

/** A stand-in for app.json's `expo` block, with the fields that must not reach the manifest. */
const appConfig = {
  name: 'FuelFlow',
  slug: 'mobile-app-native',
  scheme: 'fuelflow',
  version: '1.2.0',
  orientation: 'portrait',
  icon: './assets/icon.png',
  ios: { bundleIdentifier: 'com.fuelflowmarketplace.www', buildNumber: '237' },
  android: { package: 'com.artem.vashchuk.mobileappnative', versionCode: 2 },
  plugins: ['expo-router', 'expo-dev-client'],
  runtimeVersion: { policy: 'fingerprint' },
  owner: 'artem.vashchuk',
  updates: {
    enabled: true,
    url: 'https://api.palne.shop/api/updates/manifest',
    codeSigningMetadata: { keyid: 'main', alg: 'rsa-v1_5-sha256' },
  },
  extra: {
    router: { origin: false },
    eas: { projectId: '37e2ff06-380f-417f-8d19-de2d988c9322', buildProfile: 'production' },
    apiUrl: 'https://api.palne.shop',
    sentryDsn: 'https://key@sentry.example/1',
    cartoApiKey: 'carto-key',
  },
};

function filesUnder(dir) {
  return readdirSync(dir, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && /\.(ts|tsx|js|jsx)$/.test(entry.name))
    .map((entry) => join(entry.parentPath ?? entry.path, entry.name));
}

test('keeps what the app reads and drops the rest of the config', () => {
  const client = clientConfigForManifest(appConfig);

  assert.deepEqual(client, {
    version: '1.2.0',
    scheme: 'fuelflow',
    extra: {
      apiUrl: 'https://api.palne.shop',
      eas: { projectId: '37e2ff06-380f-417f-8d19-de2d988c9322' },
      sentryDsn: 'https://key@sentry.example/1',
      cartoApiKey: 'carto-key',
    },
  });

  for (const key of ['name', 'slug', 'ios', 'android', 'plugins', 'updates', 'owner', 'runtimeVersion']) {
    assert.equal(key in client, false, `${key} is build metadata, not a client read`);
  }
});

test('extra.router does not ride along', () => {
  // expo-router reads its origin in development; a standalone build does not, and the key was
  // never part of the read surface that survived the move to a signed OTA manifest.
  assert.equal('router' in clientConfigForManifest(appConfig).extra, false);
});

test('an absent optional key is omitted, not set to null', () => {
  const client = clientConfigForManifest({
    ...appConfig,
    extra: { apiUrl: 'https://api.palne.shop' },
  });

  assert.deepEqual(client.extra, { apiUrl: 'https://api.palne.shop' });
  assert.equal('sentryDsn' in client.extra, false);
});

test('the top-level keys are omitted when the config does not carry them', () => {
  const { scheme: _scheme, version: _version, ...withoutBoth } = appConfig;
  const client = clientConfigForManifest(withoutBoth);

  assert.equal('version' in client, false);
  assert.equal('scheme' in client, false);
});

test('every extra key the app reads is on the allowlist', () => {
  const reads = new Set();

  for (const file of [...filesUnder(join(mobileRoot, 'app')), ...filesUnder(join(mobileRoot, 'src'))]) {
    const source = readFileSync(file, 'utf8');
    for (const match of source.matchAll(/expoConfig\?*\.extra\?*\.([A-Za-z_$][\w$]*)/g)) {
      reads.add(match[1]);
    }
  }

  assert.deepEqual(
    [...reads].sort(),
    [...CLIENT_EXTRA_KEYS].sort(),
    'a Constants.expoConfig?.extra read that is not on CLIENT_EXTRA_KEYS would return undefined '
      + 'after an OTA update - add it to ota-client-config.mjs, or stop reading it'
  );
});

test('every top-level expoConfig key the app reads is on the allowlist', () => {
  const reads = new Set();

  for (const file of [...filesUnder(join(mobileRoot, 'app')), ...filesUnder(join(mobileRoot, 'src'))]) {
    const source = readFileSync(file, 'utf8');
    for (const match of source.matchAll(/expoConfig\?*\.([A-Za-z_$][\w$]*)/g)) {
      if (match[1] !== 'extra') {
        reads.add(match[1]);
      }
    }
  }

  assert.deepEqual(
    [...reads].sort(),
    [...CLIENT_TOP_LEVEL_KEYS].sort(),
    'the manifest carries version and scheme because the app reads them; a new top-level read '
      + 'has to join ota-client-config.mjs'
  );
});
