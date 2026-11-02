/**
 * Writes the brand surfaces derived from mobile/assets/icon.png: the tab icon
 * and the UI lion mask for the marketing site and the admin, the iOS home-screen
 * icon, and the Android launcher foreground. Run `npm run brand` after changing
 * the lion; the output is committed.
 *
 * It writes into admin/ and mobile/ as well as website/, which is why the check
 * that gates these bytes needs a full repository checkout and the whole CI diff.
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

import { mkdir, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { buildBrandAssets, APPLE_ICON_SIZE, ICO_SIZES, MASK_SIZE, ADAPTIVE_CANVAS } from './lib/brand-assets.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const SOURCE = resolve(root, '..', 'mobile/assets/icon.png');
/* Where each derived buffer is committed. Several keys, one buffer: the admin gets
 * the marketing site's tab icon byte for byte, because two renders of one lion are
 * two chances for one of them to quietly stop being the lion. */
const TARGETS = [
  { buf: 'favicon', file: resolve(root, 'src/app/favicon.ico') },
  { buf: 'favicon', file: resolve(root, '..', 'admin/public/favicon.ico') },
  { buf: 'appleIcon', file: resolve(root, 'src/app/apple-icon.png') },
  { buf: 'mask', file: resolve(root, 'public/lion-mask.png') },
  { buf: 'mask', file: resolve(root, '..', 'admin/src/assets/lion-mask.png') },
  { buf: 'adaptiveIcon', file: resolve(root, '..', 'mobile/assets/adaptive-icon.png') },
];

if (!existsSync(SOURCE)) {
  console.error(`[brand] the source lion is missing: ${SOURCE}`);
  console.error('[brand] this command must be run from a full repository checkout, with website/ as the');
  console.error('[brand] working directory. It cannot run inside the website image, where the build context');
  console.error('[brand] is website/ alone - which is why it is not a build hook. See the header of this file.');
  process.exit(1);
}

const built = await buildBrandAssets(SOURCE);
for (const { buf, file } of TARGETS) {
  /* Creates the directory rather than assuming it: an asset directory that is
   * empty is absent in git, and this writes into packages other than website/. */
  await mkdir(dirname(file), { recursive: true });
  await writeFile(file, built[buf]);
}

const kb = (b) => `${Math.round((b.length / 1024) * 10) / 10}KB`;
console.log(
  `[brand] lion at ${built.meta.lion} -> cropped ${built.meta.cropped}px, backdrop ${built.meta.backdrop}; ` +
  `wrote favicon.ico x2 (${ICO_SIZES.join('/')}, ${kb(built.favicon)}), ` +
  `apple-icon.png (${APPLE_ICON_SIZE}, ${kb(built.appleIcon)}), ` +
  `lion-mask.png x2 (${MASK_SIZE}, ${kb(built.mask)}), ` +
  `adaptive-icon.png (${ADAPTIVE_CANVAS}, ${kb(built.adaptiveIcon)})`,
);