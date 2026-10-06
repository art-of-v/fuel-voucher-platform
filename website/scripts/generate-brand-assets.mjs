/**
 * Writes the brand surfaces (favicon, home-screen icon, UI lion mask) derived
 * from mobile/assets/icon.png. Run `npm run brand` after changing the lion; the
 * output is committed.
 *
 * WHY THIS IS NOT IN prebuild
 *
 * It was, and it broke main. website/Dockerfile is built with `website/` as the
 * build context, so inside the image the repository is just /app - there is no
 * /mobile, and Docker cannot reach outside its context at all. Wiring this into
 * prebuild meant the production image build died on
 *
 *   Error: Input file is missing: /mobile/assets/icon.png
 *
 * The CI job that tests the website checks out the whole repository, so it was
 * green the entire time and said nothing useful. The failure only appeared on
 * the job that builds the image.
 *
 * The committed assets are therefore what the build consumes, and the drift is
 * caught by scripts/check-brand-assets.mjs instead, which reproduces these bytes
 * from the lion and fails CI if they differ. That is a stronger guarantee than a
 * build hook anyway: it fails on a committed placeholder, and it also fails when
 * the lion changes and nobody regenerated.
 *
 * It stays in predev, where the full repository is present.
 *
 * sharp is a dependency of next, so it is present on every install. It is
 * declared in devDependencies anyway: an undeclared package turning up missing
 * should not be able to break a maintainer's command.
 */

import { writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { buildBrandAssets, APPLE_ICON_SIZE, ICO_SIZES, MASK_SIZE } from './lib/brand-assets.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const SOURCE = resolve(root, '..', 'mobile/assets/icon.png');
const TARGETS = {
  favicon: resolve(root, 'src/app/favicon.ico'),
  appleIcon: resolve(root, 'src/app/apple-icon.png'),
  mask: resolve(root, 'public/lion-mask.png'),
};

if (!existsSync(SOURCE)) {
  console.error(`[brand] the source lion is missing: ${SOURCE}`);
  console.error('[brand] this command must be run from a full repository checkout, with website/ as the');
  console.error('[brand] working directory. It cannot run inside the website image, where the build context');
  console.error('[brand] is website/ alone - which is why it is not a build hook. See the header of this file.');
  process.exit(1);
}

const built = await buildBrandAssets(SOURCE);
for (const [key, path] of Object.entries(TARGETS)) {
  await writeFile(path, built[key]);
}

const kb = (b) => `${Math.round((b.length / 1024) * 10) / 10}KB`;
console.log(
  `[brand] lion at ${built.meta.lion} -> cropped ${built.meta.cropped}px, backdrop ${built.meta.backdrop}; ` +
  `wrote favicon.ico (${ICO_SIZES.join('/')}, ${kb(built.favicon)}), ` +
  `apple-icon.png (${APPLE_ICON_SIZE}, ${kb(built.appleIcon)}), ` +
  `lion-mask.png (${MASK_SIZE}, ${kb(built.mask)})`,
);