#!/usr/bin/env node
/**
 * Gate: the committed brand assets are the lion, byte for byte.
 *
 * This exists because the build hook that used to guarantee it could not run.
 * generate-brand-assets.mjs reads mobile/assets/icon.png, and website/Dockerfile
 * is built with `website/` as its build context - so inside the image there is
 * no /mobile and the generator died with
 *
 *   Error: Input file is missing: /mobile/assets/icon.png
 *
 * which took main's production image build down with it. The website CI job
 * checks out the whole repository, so it stayed green and told us nothing; only
 * the job that builds the image failed.
 *
 * So the assets are committed and consumed by the build, and the drift is caught
 * here instead. This is the stronger guarantee of the two: a build hook can only
 * tell you the assets were regenerated on that machine, whereas this fails when
 * someone commits a placeholder, and equally when the lion changes and nobody
 * re-ran `npm run brand`.
 *
 * Both failure modes have shipped here before. The favicon was stock Next.js
 * placeholder art for the life of the site, and the header and footer carried a
 * two-letter monogram, with a real lion sitting in mobile/assets the whole time.
 * Neither was wrong in review.
 *
 * Runs in the website CI job, where the full repository is present. A missing
 * lion downgrades to a warning locally, the same policy as a missing browser in
 * check-hero.mjs - but never here, because this check has nothing to say without
 * the source.
 */

import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { outDir, report, root } from './lib/harness.mjs';
import { buildBrandAssets } from './lib/brand-assets.mjs';

const SOURCE = resolve(root, '..', 'mobile/assets/icon.png');
const TARGETS = [
  {
    what: 'the tab icon',
    file: resolve(root, 'src/app/favicon.ico'),
    key: 'favicon',
    copiedTo: resolve(outDir, 'favicon.ico'),
  },
  {
    what: 'the iOS home-screen icon',
    file: resolve(root, 'src/app/apple-icon.png'),
    key: 'appleIcon',
    copiedTo: resolve(outDir, 'apple-icon.png'),
  },
  {
    what: 'the UI lion mask',
    file: resolve(root, 'public/lion-mask.png'),
    key: 'mask',
    copiedTo: resolve(outDir, 'lion-mask.png'),
  },
];

if (!existsSync(SOURCE)) {
  const msg = `[brand] the source lion is missing: ${SOURCE}`;
  if (process.env.CI) {
    console.error(msg);
    process.exit(1);
  }
  console.warn(`${msg} (skipped - run this from a full repository checkout)`);
  process.exit(0);
}

const failures = [];
const rows = [];
let built;

try {
  built = await buildBrandAssets(SOURCE);
} catch (e) {
  console.error(`[brand] could not derive the brand surfaces from the lion: ${e.message}`);
  process.exit(1);
}

for (const { what, file, key, copiedTo } of TARGETS) {
  if (!existsSync(file)) {
    failures.push(`${what} is not committed (${file}) - run "npm run brand"`);
    continue;
  }
  const onDisk = readFileSync(file);
  const fresh = built[key];
  if (!onDisk.equals(fresh)) {
    failures.push(
      `${what} does not match the lion any more - ${file} is ${onDisk.length} bytes, the lion derives` +
      ` ${fresh.length}. The lion changed and nobody re-ran "npm run brand", or a placeholder was` +
      ' committed over it.',
    );
  }

  /* The build ships out/, not src/. A generator that wrote somewhere the export
   * does not carry would pass this and still ship the old icon. */
  if (!existsSync(copiedTo)) {
    failures.push(`${what} is committed but missing from the build output (${copiedTo})`);
  } else if (!readFileSync(copiedTo).equals(onDisk)) {
    failures.push(`${what} in the build output differs from the committed file - the export is stale`);
  }

  rows.push(
    `  ${what.padEnd(26)} ${String(Math.round((onDisk.length / 1024) * 10) / 10).padStart(6)}KB  ` +
    (onDisk.equals(fresh) ? 'matches the lion' : 'DRIFTED'),
  );
}

console.log(`\n[brand] lion at ${built.meta.lion} -> cropped ${built.meta.cropped}px, backdrop ${built.meta.backdrop}`);
for (const r of rows) console.log(r);

if (failures.length) {
  console.error('\nRun "npm run brand" from a full repository checkout and commit the result.');
}
report(
  'brand',
  failures,
  'the tab icon, the home-screen icon and the UI mask are all the lion, and the build ships them.',
);