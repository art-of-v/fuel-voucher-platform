#!/usr/bin/env node
/**
 * Publish a signed over-the-air (OTA) JS update to Cloudflare R2 for the self-hosted
 * expo-updates endpoint (planning #42; backend Features/Updates/UpdatesController.cs).
 *
 * WHY THIS EXISTS (and why the private key never leaves this machine):
 *   The .NET API only *relays* pre-signed manifest bytes. All signing happens here, on the
 *   publish machine, with keys/private-key.pem — gitignored, and it must stay off every server.
 *   A stolen R2 token or a compromised API therefore cannot forge an update the app will install:
 *   the client verifies every manifest against the certificate baked into the build
 *   (certs/certificate.pem) and keeps its last-good bundle on any mismatch.
 *
 * WHAT IT DOES:
 *   0. Resolves expo.runtimeVersion. A "fingerprint" policy is recomputed here with the same tool
 *      the native build uses (expo-updates fingerprint:generate) so the descriptor is published
 *      under the exact hash the installed build asks for.
 *   1. expo export (iOS) -> dist/
 *   2. Builds an expo-updates protocol-v1 manifest from dist/metadata.json, content-addressing
 *      the JS bundle and every asset by SHA-256.
 *   3. Signs the EXACT manifest bytes (RSASSA-PKCS1-v1_5 / SHA-256) into an expo-signature value.
 *   4. Uploads the bundle + assets to R2 at their content-addressed keys, then writes the
 *      descriptor the API reads at {platform}/{runtimeVersion}/update.json =
 *      { "manifestBase64": "<base64 of the exact signed bytes>", "signature": "<expo-signature>" }.
 *
 * REQUIREMENTS (Mac, one-time): rclone configured with a scoped Object-R/W token for the public
 *   OTA bucket, the private key at keys/private-key.pem, and these env vars:
 *     OTA_RCLONE_REMOTE    e.g. r2ota:fuelflow-ota    (rclone remote:bucket to upload into)
 *     OTA_PUBLIC_BASE_URL  e.g. https://ota.palne.shop (public CDN base the manifest URLs use)
 *   Runbook: docs/DEPLOYMENT.md ("Over-the-air updates (OTA)").
 *
 * NOTE: run once and curl-verify the printed endpoint before flipping app.json
 *   expo.updates.enabled=true + url in a NEW native build — an installed build only starts
 *   verifying manifests after that signed build ships.
 */

import { execFileSync } from 'node:child_process';
import { createHash, sign } from 'node:crypto';
import { existsSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const distDir = join(mobileRoot, 'dist');
const PLATFORM = 'ios';

const die = (message) => {
  console.error(`\npublish-ota FAILED\n\n${message.trim()}\n`);
  process.exit(1);
};

const CONTENT_TYPES = {
  js: 'application/javascript',
  hbc: 'application/javascript',
  json: 'application/json',
  png: 'image/png',
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  gif: 'image/gif',
  webp: 'image/webp',
  svg: 'image/svg+xml',
  ttf: 'font/ttf',
  otf: 'font/otf',
  woff: 'font/woff',
  woff2: 'font/woff2',
};
const contentTypeFor = (ext) => CONTENT_TYPES[String(ext).toLowerCase()] ?? 'application/octet-stream';

// SHA-256 in the two encodings the protocol uses: base64url for the integrity `hash` the client
// checks, hex for the opaque, stable content-addressed `key`.
const sha256Base64Url = (buf) => createHash('sha256').update(buf).digest('base64url');
const sha256Hex = (buf) => createHash('sha256').update(buf).digest('hex');

// --- config + secrets -----------------------------------------------------------------
const remote = process.env.OTA_RCLONE_REMOTE;
const publicBaseUrl = process.env.OTA_PUBLIC_BASE_URL?.replace(/\/+$/, '');
if (!remote) die('OTA_RCLONE_REMOTE is not set (e.g. r2ota:fuelflow-ota).');
if (!publicBaseUrl) die('OTA_PUBLIC_BASE_URL is not set (e.g. https://ota.palne.shop).');

const keyPath = join(mobileRoot, 'keys', 'private-key.pem');
if (!existsSync(keyPath)) {
  die(
    `Private signing key not found at ${keyPath}.\n` +
      'It is gitignored and must stay on this machine. Generate it once with:\n' +
      '  npx expo-updates codesigning:generate --key-output-directory keys \\\n' +
      '    --certificate-output-directory certs --certificate-validity-duration-years 10 \\\n' +
      '    --certificate-common-name FuelFlow'
  );
}
const privateKey = readFileSync(keyPath, 'utf8');

const appConfig = JSON.parse(readFileSync(join(mobileRoot, 'app.json'), 'utf8')).expo ?? {};

// Resolve expo.runtimeVersion to the concrete string the client sends as `expo-runtime-version`.
// With `{ "policy": "fingerprint" }` the NATIVE BUILD bakes in a hash of the native layer (computed
// by expo-updates via @expo/fingerprint) and sends THAT. We must publish the descriptor under the
// exact same hash, so we recompute it with the very tool the build uses — never a hand-rolled hash.
// If publish-time and build-time fingerprints differ (e.g. publishing from a different checkout than
// the build came from), the app requests a runtime version nobody published and the API answers 204,
// a SILENT no-update. See docs/DEPLOYMENT.md ("Runtime version — a native fingerprint").
const usesFingerprint =
  typeof appConfig.runtimeVersion === 'object' && appConfig.runtimeVersion?.policy === 'fingerprint';
const resolveRuntimeVersion = () => {
  const rv = appConfig.runtimeVersion;
  if (typeof rv === 'string') return rv;
  if (!usesFingerprint) {
    die('expo.runtimeVersion must be a string or { "policy": "fingerprint" } in app.json.');
  }
  console.log(`> expo-updates fingerprint:generate (${PLATFORM})`);
  let out;
  try {
    out = execFileSync('npx', ['expo-updates', 'fingerprint:generate', '--platform', PLATFORM], {
      cwd: mobileRoot,
      encoding: 'utf8',
    });
  } catch (error) {
    die(
      'Could not compute the native fingerprint. Run it by hand to see why:\n' +
        `  cd mobile && npx expo-updates fingerprint:generate --platform ${PLATFORM}\n\n` +
        `${error.stdout ?? ''}${error.stderr ?? ''}`
    );
  }
  let hash;
  try {
    hash = JSON.parse(out).fingerprintHash ?? JSON.parse(out).hash;
  } catch {
    hash = out; // some versions print the bare hash instead of JSON
  }
  hash = typeof hash === 'string' ? hash.trim() : '';
  if (!hash) die(`expo-updates fingerprint:generate returned no fingerprint hash.\nRaw output:\n${out}`);
  return hash;
};

const runtimeVersion = resolveRuntimeVersion();
const { keyid = 'main', alg = 'rsa-v1_5-sha256' } = appConfig.updates?.codeSigningMetadata ?? {};

// --- 1. export the JS bundle + assets --------------------------------------------------
console.log(`> expo export (${PLATFORM})`);
execFileSync('npx', ['expo', 'export', '--platform', PLATFORM, '--output-dir', 'dist'], {
  cwd: mobileRoot,
  stdio: 'inherit',
});

const metadata = JSON.parse(readFileSync(join(distDir, 'metadata.json'), 'utf8'));
const fileMetadata = metadata?.fileMetadata?.[PLATFORM];
if (!fileMetadata?.bundle) die('dist/metadata.json has no fileMetadata.ios.bundle — did export run?');

// Uploads one file to R2 at a content-addressed key with an explicit Content-Type, then returns
// the public URL the manifest points at. --s3-no-check-bucket avoids a bucket-level HEAD that a
// scoped Object-R/W token (no bucket perms) would reject.
const uploadFile = (localPath, remoteKey, contentType) => {
  execFileSync(
    'rclone',
    [
      'copyto',
      '--s3-no-check-bucket',
      '--header-upload',
      `Content-Type: ${contentType}`,
      localPath,
      `${remote}/${remoteKey}`,
    ],
    { stdio: 'inherit' }
  );
  return `${publicBaseUrl}/${remoteKey}`;
};

// --- 2. content-address + upload the launch asset (the JS bundle) and every asset ------
const bundlePath = join(distDir, fileMetadata.bundle);
const bundleBytes = readFileSync(bundlePath);
const launchAsset = {
  hash: sha256Base64Url(bundleBytes),
  key: sha256Hex(bundleBytes),
  contentType: 'application/javascript',
  url: uploadFile(bundlePath, `bundles/${PLATFORM}/${sha256Hex(bundleBytes)}.js`, 'application/javascript'),
};

const assets = (fileMetadata.assets ?? []).map((asset) => {
  const bytes = readFileSync(join(distDir, asset.path));
  const contentType = contentTypeFor(asset.ext);
  return {
    hash: sha256Base64Url(bytes),
    key: sha256Hex(bytes),
    contentType,
    fileExtension: `.${asset.ext}`,
    url: uploadFile(join(distDir, asset.path), `assets/${sha256Hex(bytes)}`, contentType),
  };
});

// --- 3. assemble + sign the manifest ---------------------------------------------------
// id is derived from the bundle hash, so re-publishing an identical bundle yields a stable id.
const id = sha256Hex(bundleBytes)
  .slice(0, 32)
  .replace(/(.{8})(.{4})(.{4})(.{4})(.{12})/, '$1-$2-$3-$4-$5');

const manifest = {
  id,
  createdAt: new Date().toISOString(),
  runtimeVersion,
  launchAsset,
  assets,
  metadata: {},
  // expoClient is the config the app reads via Constants.expoConfig after an update. app.json's
  // expo block is the no-dependency source; it carries extra.apiUrl, version, scheme, etc.
  extra: { expoClient: appConfig },
};

// The signature is over these EXACT bytes; the API relays them verbatim and the client verifies
// the same bytes, so nothing downstream may re-serialise the manifest. RSA keys sign with
// PKCS#1 v1.5 padding by default, which is exactly alg "rsa-v1_5-sha256".
const manifestBytes = Buffer.from(JSON.stringify(manifest), 'utf8');
const signatureB64 = sign('RSA-SHA256', manifestBytes, privateKey).toString('base64');
const signature = `sig="${signatureB64}", keyid="${keyid}", alg="${alg}"`;

// --- 4. write + upload the descriptor the API reads ------------------------------------
const descriptor = { manifestBase64: manifestBytes.toString('base64'), signature };
const tmpFile = join(mkdtempSync(join(tmpdir(), 'ff-ota-')), 'update.json');
writeFileSync(tmpFile, JSON.stringify(descriptor));
const descriptorKey = `${PLATFORM}/${runtimeVersion}/update.json`;
uploadFile(tmpFile, descriptorKey, 'application/json');

console.log(
  `\npublish-ota OK\n` +
    `  runtimeVersion : ${runtimeVersion}${usesFingerprint ? '  (fingerprint)' : ''}\n` +
    `  update id      : ${id}\n` +
    `  assets         : ${assets.length}\n` +
    `  descriptor     : ${publicBaseUrl}/${descriptorKey}\n\n` +
    (usesFingerprint
      ? `PARITY: this must equal the runtime version the target build baked in, or the app gets a\n` +
        `silent 204. Confirm on the build's checkout:\n` +
        `  /usr/libexec/PlistBuddy -c "Print :EXUpdatesRuntimeVersion" ios/*/Supporting/Expo.plist\n\n`
      : '') +
    `Verify the API relays it (once OTA_PUBLIC_BASE_URL is set on the server):\n` +
    `  curl -sS -D - -o /dev/null https://api.palne.shop/api/updates/manifest \\\n` +
    `    -H "expo-platform: ${PLATFORM}" -H "expo-runtime-version: ${runtimeVersion}"\n`
);
