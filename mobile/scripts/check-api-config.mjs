#!/usr/bin/env node
/**
 * Gate: every distributable EAS build profile must declare EXPO_PUBLIC_API_URL.
 *
 * EAS cloud builders never see gitignored `.env` files, so an app built without a declared
 * URL falls back through its resolution chain until it dials `http://localhost:5000` — i.e.,
 * itself. That is exactly what shipped to TestFlight in Aug 2026 after commit f80cdb9 removed
 * the previous hardcoded fallback from app.json ("harden containers and remove hardcoded
 * URLs"): users saw nothing but "INITIALIZING NETWORK..." while the backend was healthy.
 *
 * Two rules are enforced here:
 *   1. Every profile in eas.json `build` declares EXPO_PUBLIC_API_URL as https:// (no trailing
 *      slash — apiClient concatenates paths that already start with "/").
 *   2. app.json expo.extra.apiUrl is present and EQUAL to every profile's EXPO_PUBLIC_API_URL.
 *      It is the embedded copy resolveApiBaseUrl() falls back to when the env var is not inlined
 *      into the bundle (e.g. a raw Xcode Archive that never saw the shell env). The value may
 *      live in both eas.json and app.json, but the two copies must never drift — the Aug 2026
 *      outage was exactly such a drift.
 *
 * Runtime counterpart: resolveApiBaseUrl() in mobile/src/core/api/apiClient.ts resolves the URL
 * from EXPO_PUBLIC_API_URL, then expo.extra.apiUrl, then a hardcoded production constant. It
 * deliberately never throws: it runs at module load, before React mounts, where a throw would be
 * an uncatchable launch SIGABRT rather than a catchable error.
 *
 * Escape hatch: set FUELFLOW_ACK_MISSING_API_URL=true to downgrade failures to warnings.
 * Setting it is a recorded decision to ship a build that cannot reach its backend -- it must
 * appear in CI config or the operator's shell, where a reviewer can see it.
 */

import { readFileSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const acknowledged = process.env.FUELFLOW_ACK_MISSING_API_URL === 'true';

const fail = (message) => {
  const label = acknowledged ? 'WARNING (acknowledged)' : 'FAILED';
  console.error(`\nAPI config check ${label}\n`);
  console.error(message.trim());
  if (acknowledged) {
    console.error(
      '\nFUELFLOW_ACK_MISSING_API_URL=true is set, so this is not blocking the build.\n' +
        'This is an accepted risk, not a resolved one.\n'
    );
    process.exit(0);
  }
  process.exit(1);
};

const easJsonPath = resolve(mobileRoot, 'eas.json');
if (!existsSync(easJsonPath)) {
  console.error(`API config check FAILED\n\neas.json not found at ${easJsonPath}`);
  process.exit(1);
}

const eas = JSON.parse(readFileSync(easJsonPath, 'utf8'));
const profiles = Object.entries(eas?.build ?? {});

if (profiles.length === 0) {
  fail('No build profiles found in eas.json.');
}

const problems = [];

for (const [name, profile] of profiles) {
  const url = profile?.env?.EXPO_PUBLIC_API_URL;

  if (!url) {
    problems.push(
      `Profile "${name}" does not declare EXPO_PUBLIC_API_URL. Cloud builds of this profile ` +
        'will dial localhost instead of your backend.'
    );
    continue;
  }

  if (!url.startsWith('https://')) {
    problems.push(
      `Profile "${name}": EXPO_PUBLIC_API_URL must use https:// (got "${url}"). ` +
        'iOS App Transport Security rejects plain http://, so requests would still fail.'
    );
  }

  if (url.endsWith('/')) {
    problems.push(
      `Profile "${name}": EXPO_PUBLIC_API_URL must not end with "/" — apiClient joins ` +
        `paths that already start with "/", so "${url}" would produce doubled slashes.`
    );
  }
}

const appJsonPath = resolve(mobileRoot, 'app.json');
if (existsSync(appJsonPath)) {
  const extraApiUrl = JSON.parse(readFileSync(appJsonPath, 'utf8'))?.expo?.extra?.apiUrl;

  if (!extraApiUrl) {
    problems.push(
      'app.json is missing expo.extra.apiUrl. That is the embedded copy resolveApiBaseUrl() ' +
        'falls back to when EXPO_PUBLIC_API_URL is not inlined into the bundle — e.g. a raw Xcode ' +
        'Archive. Without it, such a build has no configured backend until the hardcoded ' +
        'last-resort constant. Set expo.extra.apiUrl to the same value as the eas.json profiles.'
    );
  } else {
    // Single source of truth is enforced by EQUALITY, not by absence: the URL is allowed to live
    // in both eas.json (consumed by EAS cloud builds) and app.json extra (embedded into every
    // build path, incl. a raw Xcode Archive), but the two copies must never drift.
    for (const [name, profile] of profiles) {
      const url = profile?.env?.EXPO_PUBLIC_API_URL;
      if (url && url !== extraApiUrl) {
        problems.push(
          `expo.extra.apiUrl ("${extraApiUrl}") does not match profile "${name}" ` +
            `EXPO_PUBLIC_API_URL ("${url}"). These are the same setting expressed in two build ` +
            'paths and must be identical; update one so they agree.'
        );
      }
    }
  }
}

if (problems.length > 0) {
  fail(
    problems.join('\n\n') +
      '\n\nTo fix:\n' +
      '  1. Add "env": { "EXPO_PUBLIC_API_URL": "https://your-backend.example.com" } to each\n' +
      '     build profile in mobile/eas.json.\n' +
      '  2. Rebuild — the value is baked into the JS bundle at build time; changing eas.json\n' +
      '     alone does nothing to an already-built binary.'
  );
}

console.log(
  `API config check passed: ${profiles.map(([name]) => name).join(', ')} profiles declare EXPO_PUBLIC_API_URL.`
);
