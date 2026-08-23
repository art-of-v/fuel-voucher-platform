#!/usr/bin/env node
/**
 * FF-03 gate: refuse an over-the-air update channel that is enabled but unsigned.
 *
 * `expo.updates.url` in app.json lets a published JavaScript bundle replace the running app's code
 * on every installed device, including the checkout and payment screens. Without
 * `expo.updates.codeSigningCertificate`, the client accepts any manifest the channel serves, so the
 * EAS publish token is the only thing standing between an attacker and arbitrary code execution in
 * the app. A leaked or misused token is then indistinguishable from a legitimate release.
 *
 * This check is deliberately a hard failure. It mirrors the backend's startup posture in
 * backend/src/FuelFlow.API/Program.cs:221-233: a known-missing control may be accepted, but only
 * explicitly and visibly, never by default and never silently.
 *
 * Escape hatch: set FUELFLOW_ACK_UNSIGNED_OTA=true to downgrade the failure to a warning. Setting it
 * is a recorded decision to ship an unsigned update channel -- it must appear in the CI config or the
 * operator's shell, where a reviewer can see it.
 */

import { readFileSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const appJsonPath = resolve(mobileRoot, 'app.json');

const fail = (message) => {
  const acknowledged = process.env.FUELFLOW_ACK_UNSIGNED_OTA === 'true';
  const label = acknowledged ? 'WARNING (acknowledged)' : 'FAILED';
  console.error(`\nOTA update signing check ${label}\n`);
  console.error(message.trim());
  if (acknowledged) {
    console.error(
      '\nFUELFLOW_ACK_UNSIGNED_OTA=true is set, so this is not blocking the build.\n' +
        'This is an accepted risk, not a resolved one.\n'
    );
    process.exit(0);
  }
  console.error(
    '\nTo fix (this is the real fix, and it requires a new native build):\n' +
      '  1. npx expo-updates codesigning:generate --key-output-dir keys --certificate-output-dir certs \\\n' +
      '       --certificate-validity-duration-years 10 --certificate-common-name FuelFlow\n' +
      '  2. npx expo-updates codesigning:configure --certificate-input-dir certs --key-input-dir keys\n' +
      '  3. Keep keys/ out of git. Store the private key in the EAS secret store.\n' +
      '  4. Build and release a new native binary to both stores. Signing is enforced by the client,\n' +
      '     so an already-installed build will not start verifying manifests on its own.\n\n' +
      'To accept the risk instead, set FUELFLOW_ACK_UNSIGNED_OTA=true.\n'
  );
  process.exit(1);
};

if (!existsSync(appJsonPath)) {
  console.error(`OTA update signing check FAILED\n\napp.json not found at ${appJsonPath}`);
  process.exit(1);
}

const updates = JSON.parse(readFileSync(appJsonPath, 'utf8'))?.expo?.updates ?? {};

if (updates.enabled === false) {
  console.log('OTA update signing check passed: expo.updates.enabled is false, no update channel.');
  process.exit(0);
}

if (!updates.url) {
  console.log('OTA update signing check passed: no expo.updates.url, so no update channel.');
  process.exit(0);
}

if (!updates.codeSigningCertificate) {
  fail(
    `expo.updates.url is set to ${updates.url}\n` +
      'but expo.updates.codeSigningCertificate is absent, so the app accepts unsigned update\n' +
      'manifests. Anyone able to publish to this channel can run arbitrary code in the app.'
  );
}

const certificatePath = resolve(mobileRoot, updates.codeSigningCertificate);
if (!existsSync(certificatePath)) {
  fail(
    `expo.updates.codeSigningCertificate points at ${updates.codeSigningCertificate}\n` +
      `which does not exist (resolved to ${certificatePath}).\n` +
      'A build with a dangling certificate path fails at bundle time; do not commit the key\n' +
      'reference before the certificate is in place.'
  );
}

console.log(
  `OTA update signing check passed: update manifests are verified against ${updates.codeSigningCertificate}.`
);
