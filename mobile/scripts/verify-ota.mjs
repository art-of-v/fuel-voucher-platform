#!/usr/bin/env node
/**
 * Read back what an installed build is actually being served, and prove the bytes.
 *
 * WHY THIS EXISTS (and why it is not just a curl):
 *   A `200` from the manifest endpoint only says *a* manifest exists for that runtime version. It
 *   says nothing about whether the fix is inside the bundle the manifest points at, whether the
 *   signature on the wire matches the certificate the build carries, or whether the API relays the
 *   same bytes the bucket holds. Every one of those has been the difference between "published" and
 *   "the fleet got it" - see the 2026-10-29 outage, where the bundle shipped a guard that did not
 *   run. This script answers all four in one read-only pass, from any machine: it needs no rclone
 *   remote and never touches the private key, which lives on the publish Mac alone.
 *
 * WHAT IT CHECKS:
 *   1. {otaBase}/{platform}/{runtimeVersion}/update.json resolves and is a descriptor
 *      ({manifestBase64, signature}).
 *   2. The signature verifies over the EXACT manifest bytes against the public key of the
 *      certificate the app is configured with (app.json -> updates.codeSigningCertificate).
 *   3. The launch asset downloads, its SHA-256 equals the manifest `hash`, the URL is
 *      content-addressed with that same digest, and it is Hermes bytecode for a release export.
 *   4. Every --expect-string marker is present in the bundle - the only way to prove a specific
 *      change shipped. ASCII only: a Hermes string table stores non-ASCII runs as UTF-16, so a
 *      Ukrainian literal will not match a plain byte search even when it is in there.
 *   5. A sample of assets hash-match (their digests, not just their count).
 *   6. The API relay (/api/updates/manifest, asked with the headers that build sends) answers 200
 *      and not 204, carries expo-protocol-version: 1, and its manifest part is byte-identical to
 *      the bucket's with a signature that verifies over those relayed bytes.
 *
 * WHAT IT CANNOT PROVE, so do not read it as a device pass:
 *   - That the *installed binary* trusts this certificate. It trusts the certificate baked in at
 *     build time; if that key pair is ever rotated, old builds reject a bundle this script calls
 *     valid. The device is the only witness to that.
 *   - That the bundle runs. A missing native module is a runtime fact, invisible in bytes - the
 *     fleet's `Cannot find native module 'ExpoCrypto'` abort shipped through every check above.
 *   - That the device took it: expo-updates downloads in the background and swaps in on the NEXT
 *     launch, so a fresh publish reads as unchanged until the app has been restarted twice.
 *
 * USAGE:
 *   npm run verify:ota -- --runtime-version <fingerprint> [--platform ios] [--assets 3]
 *       [--expect-commit <sha>] [--expect-string <literal> ...] [--ota-base https://…]
 *
 *   The runtime version is deliberately required, never defaulted: this is the check that catches
 *   publishing under the wrong hash, and a script that guessed the hash would guess today's tree
 *   and bless exactly the mistake it exists to find. Read it off the publish output, or from the
 *   manifest log line "Manifest request for ios/<hash>".
 *
 *   --ota-base overrides the public bucket origin (default https://ota.palne.shop, or
 *   $OTA_PUBLIC_BASE_URL when set) for verifying a staging channel.
 */

import { createHash, verify as verifySignature, X509Certificate } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { servedStampFrom } from './ota-publish-guard.mjs';

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const die = (message) => {
  console.error(`\nverify-ota FAILED\n\n${message.trim()}\n`);
  process.exit(1);
};

const argv = process.argv.slice(2);
const readArg = (name) => {
  const inline = argv.find((a) => a.startsWith(`${name}=`));
  if (inline) return inline.slice(name.length + 1);
  const index = argv.indexOf(name);
  return index >= 0 ? argv[index + 1] : undefined;
};
// Repeating a flag is the point (--expect-string is a list), so this walks argv by index —
// `argv.indexOf(a)` would answer with the first occurrence for every occurrence and turn four
// different markers into the same one printed four times.
const readAll = (name) => {
  const values = [];
  argv.forEach((arg, index) => {
    if (arg === name) {
      const value = argv[index + 1];
      if (value && !value.startsWith('--')) values.push(value);
    } else if (arg.startsWith(`${name}=`)) {
      values.push(arg.slice(name.length + 1));
    }
  });
  return values;
};

const runtimeVersion = readArg('--runtime-version')?.trim();
if (!runtimeVersion) {
  die(
    'Which build are you verifying? Pass the runtime version it asks for:\n' +
      '  npm run verify:ota -- --runtime-version <fingerprint>\n' +
      'It is printed by publish-ota and logged by the manifest endpoint as "Manifest request for ios/<hash>".',
  );
}
const platform = readArg('--platform')?.trim() ?? 'ios';
const assetSample = Number(readArg('--assets') ?? 3);
const markers = readAll('--expect-string');
const otaBase = (
  readArg('--ota-base') ??
  process.env.OTA_PUBLIC_BASE_URL ??
  'https://ota.palne.shop'
).replace(/\/+$/, '');

const appConfig = JSON.parse(readFileSync(join(mobileRoot, 'app.json'), 'utf8')).expo ?? {};
const updates = appConfig.updates ?? {};
const apiManifestUrl = updates.url;
if (!apiManifestUrl) {
  die('app.json has no expo.updates.url - the relay this check reads is defined there.');
}
const certificatePath = join(
  mobileRoot,
  (updates.codeSigningCertificate ?? './certs/certificate.pem').replace(/^\.\//, ''),
);
if (!existsSync(certificatePath)) {
  die(
    `The build's certificate is not at ${certificatePath} (app.json -> updates.codeSigningCertificate).`,
  );
}
const certificate = new X509Certificate(readFileSync(certificatePath, 'utf8'));
const expectedKeyid = updates.codeSigningMetadata?.keyid;

const problems = [];
const note = (label, ok, detail) => {
  if (!ok) problems.push(`${label}: ${detail}`);
  console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${label.padEnd(13)} ${detail}`);
};

const sha256 = (buf) => ({
  b64url: createHash('sha256').update(buf).digest('base64url'),
  b64: createHash('sha256').update(buf).digest('base64'),
  hex: createHash('sha256').update(buf).digest('hex'),
});
// The manifest stores the digest base64url, but a hand-edited or re-encoded descriptor has been
// seen with padding or hex, and a mismatch is the one failure nobody should have to debug from a
// bare boolean - so all three encodings are compared and the winner is named.
const digestMatches = (expected, buf) => {
  const s = sha256(buf);
  if (!expected) return { ok: false, found: 'no hash in the manifest', s };
  const padded = expected.replace(/=+$/, '');
  const fromB64Url = Buffer.from(padded, 'base64url').toString('base64').replace(/=+$/, '');
  const ok = padded === s.b64url || padded === s.b64 || padded === s.hex || padded === fromB64Url;
  return { ok, found: `sha256=${s.hex}`, s };
};

const parseSignature = (value) => {
  const sig = /sig="([^"]+)"/.exec(value)?.[1];
  const keyid = /keyid="([^"]+)"/.exec(value)?.[1];
  const alg = /alg="([^"]+)"/.exec(value)?.[1];
  return { sig, keyid, alg };
};
const signedByOurCertificate = (bytes, signatureValue) => {
  const { sig, keyid, alg } = parseSignature(signatureValue);
  if (!sig) return { ok: false, detail: 'no sig="…" in the expo-signature value' };
  let verified = false;
  try {
    verified = verifySignature('sha256', bytes, certificate.publicKey, Buffer.from(sig, 'base64'));
  } catch (error) {
    return { ok: false, detail: `${error.message}` };
  }
  const keyidOk = !expectedKeyid || keyid === expectedKeyid;
  return {
    ok: verified && keyidOk,
    detail:
      `${verified ? 'VERIFIES' : 'INVALID'} over the manifest bytes, ${alg ?? 'alg unknown'}, keyid=${keyid ?? '?'}` +
      (keyidOk ? '' : ` (app.json asks for keyid "${expectedKeyid}")`) +
      ` against ${certificate.subject.replace('CN=', '')} valid to ${certificate.validTo}`,
  };
};

const get = async (url, headers) => {
  const response = await fetch(url, { headers: { 'cache-control': 'no-cache', ...headers } });
  const body = Buffer.from(await response.arrayBuffer());
  return { status: response.status, headers: response.headers, body };
};

// --- 1-2. the descriptor in the bucket, and the signature over its exact bytes ------------------
const descriptorUrl = `${otaBase}/${platform}/${runtimeVersion}/update.json`;
console.log(`\n> ${platform} runtime version ${runtimeVersion}`);
const descriptorResponse = await get(descriptorUrl);
if (descriptorResponse.status !== 200) {
  die(
    `${descriptorUrl} answered ${descriptorResponse.status}, not 200.\n` +
      'No descriptor for that hash means the fleet on it gets a silent 204: nothing was published ' +
      'under the runtime version that build asks for.',
  );
}
let descriptor;
try {
  descriptor = JSON.parse(descriptorResponse.body.toString('utf8'));
} catch {
  die(`${descriptorUrl} is not JSON - the object in the bucket is not a descriptor.`);
}
if (!descriptor.manifestBase64 || !descriptor.signature) {
  die('The descriptor is missing manifestBase64 or signature; publish-ota writes both.');
}
const manifestBytes = Buffer.from(descriptor.manifestBase64, 'base64');
const manifest = JSON.parse(manifestBytes.toString('utf8'));
const bucketSignature = signedByOurCertificate(manifestBytes, descriptor.signature);
note('signature', bucketSignature.ok, bucketSignature.detail);

const manifestRv = manifest.runtimeVersion;
if (manifestRv && manifestRv !== runtimeVersion) {
  problems.push(
    `manifest declares runtimeVersion ${manifestRv}, published under ${runtimeVersion}`,
  );
}
console.log(
  `       manifest id=${manifest.id} createdAt=${manifest.createdAt} assets=${manifest.assets?.length ?? '?'} ` +
    `${manifestRv && manifestRv !== runtimeVersion ? `!! rv=${manifestRv}` : ''}`,
);

// The publisher stamps the commit into the SIGNED manifest (F-13), so where a stamp exists it is
// the strongest claim available about what shipped. A bundle published by a tree whose publisher
// predates that guard simply carries none - reported as a fact, not a failure, because the fleet's
// build-236 channel is published from exactly such a tree.
const stamp = servedStampFrom(descriptor);
const expectedCommit = readArg('--expect-commit')?.trim();
if (stamp.commit) {
  const shortCommit = stamp.commit.slice(0, 8);
  const matches =
    !expectedCommit ||
    stamp.commit.startsWith(expectedCommit) ||
    expectedCommit.startsWith(shortCommit);
  note(
    'built from',
    matches,
    `${shortCommit}${stamp.committedAt ? ` (${stamp.committedAt})` : ''}` +
      (matches ? '' : ` — expected ${expectedCommit}`),
  );
} else {
  console.log(
    '       built from    no commit stamped (a publisher before the F-13 guard) — --expect-string is the proof here',
  );
  if (expectedCommit) {
    problems.push(
      `--expect-commit ${expectedCommit} cannot be checked: the served manifest carries no commit`,
    );
  }
}

// --- 3-4. the bundle: content address, bytecode, and the markers that prove the change ----------
const bundleUrl = manifest.launchAsset?.url;
if (!bundleUrl) die('The manifest has no launchAsset.url.');
const bundle = (await get(bundleUrl)).body;
const bundleDigest = digestMatches(manifest.launchAsset.hash, bundle);
note('bundle hash', bundleDigest.ok, `${bundle.length} B, ${bundleDigest.found}`);
const urlCarriesDigest = bundleUrl.includes(sha256(bundle).hex);
note(
  'bundle key',
  urlCarriesDigest,
  urlCarriesDigest
    ? 'URL is content-addressed with that digest'
    : 'URL does not contain the digest',
);

// c61fbc03 is the Hermes bytecode magic a release export produces. Plain JS means the export ran
// without Hermes and the build will not execute it.
const HERMES_MAGIC = 'c61fbc03';
const magic = bundle.subarray(0, 4).toString('hex');
note(
  'bundle engine',
  magic === HERMES_MAGIC || magic === '03bc1fc6',
  `magic ${magic} (${magic === HERMES_MAGIC || magic === '03bc1fc6' ? 'Hermes bytecode' : 'not Hermes - a JS source bundle would not run here'})`,
);

if (markers.length) {
  const haystack = bundle.toString('latin1');
  const missing = markers.filter((m) => !haystack.includes(m));
  note(
    'markers',
    missing.length === 0,
    missing.length === 0
      ? `${markers.length} expected literal(s) present: ${markers.join(', ')}`
      : `absent from the bundle: ${missing.join(', ')} — the change is not in what shipped`,
  );
}

// --- 5. a sample of assets, by digest -----------------------------------------------------------
const assets = manifest.assets ?? [];
if (!(assetSample > 0) || assets.length === 0) {
  console.log(
    `  ok   assets         skipped (--assets ${assetSample}, ${assets.length} in the manifest)`,
  );
} else {
  const picked = assets.slice(0, Math.min(assetSample, assets.length));
  const bad = [];
  for (const asset of picked) {
    const body = (await get(asset.url)).body;
    if (!digestMatches(asset.hash, body).ok) bad.push(asset.key);
  }
  note(
    'assets',
    bad.length === 0,
    `${picked.length}/${assets.length} downloaded, ${bad.length ? `hash mismatch: ${bad.join(', ')}` : 'all hashes match'}`,
  );
}

// --- 6. the relay the device actually talks to --------------------------------------------------
const relay = await get(apiManifestUrl, {
  'expo-platform': platform,
  'expo-runtime-version': runtimeVersion,
  'expo-protocol-version': '1',
});
// 204 is the shape of "no update", and it is indistinguishable from "already current" - the failure
// mode this whole check exists to catch, so it gets its own message rather than a generic != 200.
if (relay.status === 204) {
  problems.push(
    `the API answered 204 for ${platform}/${runtimeVersion}: nothing published under that hash`,
  );
}
const contentType = relay.headers.get('content-type') ?? '';
const protocolVersion = relay.headers.get('expo-protocol-version');
note(
  'api relay',
  relay.status === 200 && contentType.includes('multipart/mixed') && protocolVersion === '1',
  `${relay.status}, ${contentType || 'no content-type'}, expo-protocol-version=${protocolVersion ?? '-'}, ${relay.body.length} B`,
);

const boundary = /boundary="?([^";]+)"?/.exec(contentType)?.[1];
if (!boundary) {
  problems.push(
    'the relay response carries no multipart boundary; the manifest part cannot be read',
  );
} else {
  const parts = relay.body
    .toString('binary')
    .split(`--${boundary}`)
    .map((part) => part.replace(/^\r\n/, ''))
    .filter((part) => part.trim() && part.trim() !== '--')
    .map((part) => {
      const split = part.indexOf('\r\n\r\n');
      return {
        head: part.slice(0, split),
        payload: Buffer.from(part.slice(split + 4).replace(/\r\n$/, ''), 'binary'),
      };
    });
  const manifestPart = parts.find((part) => part.payload[0] === 0x7b);
  if (!manifestPart) {
    problems.push('no JSON manifest part in the relayed multipart body');
  } else {
    const sameBytes = manifestPart.payload.equals(manifestBytes);
    const relaySignatureValue = /expo-signature:\s*(.+)/i.exec(manifestPart.head)?.[1] ?? '';
    const relaySignature = signedByOurCertificate(manifestPart.payload, relaySignatureValue);
    note(
      'relay bytes',
      sameBytes,
      sameBytes
        ? "the relayed manifest is byte-identical to the bucket's"
        : 'the relay serves DIFFERENT bytes than the bucket',
    );
    note('relay sig', relaySignature.ok, relaySignature.detail);
  }
}

if (problems.length) {
  console.error(
    `\nverify-ota FAILED (${problems.length} problem${problems.length === 1 ? '' : 's'})\n`,
  );
  for (const problem of problems) console.error(`  - ${problem}`);
  console.error('');
  process.exit(1);
}
console.log(
  '\nverify-ota OK — the bytes this build is served are signed by this certificate and contain what you asked for.\n',
);
