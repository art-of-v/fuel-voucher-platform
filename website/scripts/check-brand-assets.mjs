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

import sharp from 'sharp';

import { outDir, report, root } from './lib/harness.mjs';
import { buildBrandAssets, icoFrame } from './lib/brand-assets.mjs';
import { jawNotchPx, measureLegibility } from './lib/legibility.mjs';
import { SMALL_ICO_SIZES } from './lib/small-lion.mjs';

const SOURCE = resolve(root, '..', 'mobile/assets/icon.png');
/* `copiedTo` is the file the website export ships, where a committed asset the
 * build drops out of is still a missing icon. The admin and Android surfaces are
 * not part of this export, so they carry `referencedBy` instead: the declaration
 * that makes the byte load. A regenerated icon nothing points at is an icon that
 * does not exist. */
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
  {
    what: 'the admin UI lion mask',
    file: resolve(root, '..', 'admin/src/assets/lion-mask.png'),
    key: 'mask',
    referencedBy: { file: resolve(root, '..', 'admin/src/index.css'), needle: 'lion-mask.png' },
  },
  {
    what: 'the admin tab icon',
    file: resolve(root, '..', 'admin/public/favicon.ico'),
    key: 'favicon',
    referencedBy: { file: resolve(root, '..', 'admin/index.html'), needle: 'favicon.ico' },
  },
  {
    what: 'the Android launcher foreground',
    file: resolve(root, '..', 'mobile/assets/adaptive-icon.png'),
    key: 'adaptiveIcon',
    referencedBy: { file: resolve(root, '..', 'mobile/app.json'), needle: 'adaptive-icon.png' },
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
const marketingMark = resolve(root, 'public/fuelflow-logo.webp');
const marketingLockup = resolve(root, 'src/components/BrandLockup.tsx');
if (!existsSync(marketingMark)) {
  failures.push(`the marketing Fuel Flow logo is not committed (${marketingMark})`);
} else if (!existsSync(marketingLockup)
  || !readFileSync(marketingLockup, 'utf8').includes('/fuelflow-logo.webp')) {
  failures.push(`the marketing Fuel Flow logo is committed but the brand lockup does not load it`);
}
let built;

try {
  built = await buildBrandAssets(SOURCE);
} catch (e) {
  console.error(`[brand] could not derive the brand surfaces from the lion: ${e.message}`);
  process.exit(1);
}

for (const { what, file, key, copiedTo, referencedBy } of TARGETS) {
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
  if (copiedTo) {
    if (!existsSync(copiedTo)) {
      failures.push(`${what} is committed but missing from the build output (${copiedTo})`);
    } else if (!readFileSync(copiedTo).equals(onDisk)) {
      failures.push(`${what} in the build output differs from the committed file - the export is stale`);
    }
  }

  /* And something has to load it. This is the half the admin missed: its favicon
   * sat outside this pipeline entirely, so it could be any bytes at all and the
   * name it shipped under did not even match the format inside. */
  if (referencedBy) {
    if (!existsSync(referencedBy.file)) {
      failures.push(`${what} is committed but nothing declares it (${referencedBy.file})`);
    } else if (!readFileSync(referencedBy.file, 'utf8').includes(referencedBy.needle)) {
      failures.push(
        `${what} is committed but ${referencedBy.file} does not reference "${referencedBy.needle}" - ` +
        'the surface is derived and gated, and nothing loads it.',
      );
    }
  }

  rows.push(
    `  ${what.padEnd(32)} ${String(Math.round((onDisk.length / 1024) * 10) / 10).padStart(6)}KB  ` +
    (onDisk.equals(fresh) ? 'matches the lion' : 'DRIFTED'),
  );
}

console.log(`\n[brand] lion at ${built.meta.lion} -> cropped ${built.meta.cropped}px, backdrop ${built.meta.backdrop}`);
for (const r of rows) console.log(r);

/* The small end, measured rather than asserted.
 *
 * Byte equality above already says the tab frames are the cut and not the lion
 * resampled - the cut is what the generator derives. What it cannot say is
 * whether the cut still READS, and that is the whole point of having a second
 * artwork: planning #852 measured the resampled lion at 16px as 95.8% mud, and a
 * future redraw of either the lion or the cut can quietly put it back there
 * while every byte still matches.
 *
 * So the two shipped frames are read back out of the packed ICO and measured on
 * the backdrop they sit on: the share of drawn pixels that are a wash rather
 * than a line, and the open jaw. The floors are generous on purpose - they are
 * set to catch "this is the drawing again" (86.9% and 84.4% today), not to pin
 * the exact numbers of one design. */
/* Per size, because the floors are: a 16px slot has a quarter of the canvas a
 * 32px one has. The keys are the sizes the cut is used at - `SMALL_ICO_SIZES`
 * decides which frames those are, so adding a size to the cut without a floor
 * here fails loudly rather than silently shipping an unmeasured frame. */
const LEGIBILITY = {
  16: { mudPct: 55, notchPx: 3 },
  32: { mudPct: 35, notchPx: 5 },
};
const smallRows = [];
const backdropMatch = built.meta.backdrop.match(/(\d+),(\d+),(\d+)/);
const backdrop = { r: Number(backdropMatch[1]), g: Number(backdropMatch[2]), b: Number(backdropMatch[3]) };

for (const size of SMALL_ICO_SIZES) {
  const floor = LEGIBILITY[size];
  if (!floor) {
    failures.push(`the small cut is used at ${size}px but no legibility floor is set for it`);
    continue;
  }
  const { mudPct, notchPx } = floor;
  const frame = icoFrame(built.favicon, size);
  if (!frame) {
    failures.push(`the tab icon has no ${size}px frame`);
    continue;
  }
  const { data, info } = await sharp(frame)
    .flatten({ background: backdrop })
    .removeAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });
  const m = measureLegibility(data, info.width, info.height, backdrop, info.channels);
  const notch = jawNotchPx(data, info.width, info.height, backdrop, info.channels);

  if (m.mudPct > mudPct) {
    failures.push(
      `the ${size}px tab frame is ${m.mudPct.toFixed(1)}% mud (the floor is ${mudPct}%) - ` +
      'the small cut is no longer the cut, or it has been redrawn too fine for the slot',
    );
  }
  if (notch < notchPx) {
    failures.push(
      `the ${size}px tab frame has no open jaw (${notch}px, the floor is ${notchPx}px) - ` +
      'a mark whose mouth closes in the resize is a mark with no jaw',
    );
  }
  smallRows.push(
    `  ${`the ${size}px tab frame`.padEnd(32)} ${String(Math.round((frame.length / 1024) * 10) / 10).padStart(6)}KB  ` +
    `mud ${m.mudPct.toFixed(1)}%, jaw ${notch}px`,
  );
}

if (smallRows.length) {
  console.log('\n[small] the tab frames, measured on the tab backdrop');
  for (const r of smallRows) console.log(r);
}

if (failures.length) {
  console.error('\nRun "npm run brand" from a full repository checkout and commit the result.');
}
report(
  'brand',
  failures,
  'the marketing site loads its approved Fuel Flow lockup, while every lion used by app/admin icons remains derived from the canonical source.',
);