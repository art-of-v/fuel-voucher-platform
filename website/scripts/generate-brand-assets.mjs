/**
 * Renders every brand surface from the one lion in the repo: mobile/assets/icon.png.
 *
 * Why a generator and not committed hand-made files: the tab icon shipped as
 * stock Next.js placeholder art for the entire life of the site, and the header
 * and footer carried an "FF" monogram. Nothing about either is wrong in code
 * review - the favicon file is present, valid and the right shape, it is simply
 * not the brand. Deriving them from the same asset the mobile app uses means the
 * placeholder cannot come back and the mark cannot drift.
 *
 * This mirrors generate-store-qr.mjs, which does the same for the QR: run before
 * dev/build, with the output committed so a fresh checkout works even if the
 * hooks are bypassed.
 *
 * Outputs
 *   src/app/favicon.ico    16/32/48/256, rounded corners
 *   src/app/apple-icon.png 180px, square - iOS applies its own mask
 *   public/lion-mask.png   the UI mark: white, alpha = the drawing's own luminance
 *
 * Details that are easy to get wrong:
 *
 *   - The source is 1024x1024 with the lion sitting inside it, so a plain
 *     downscale ships an icon that is 70% empty. The lion's bounding box is
 *     measured instead, and everything is cropped to it.
 *
 *   - The backdrop is sampled from the source's border rather than forced to
 *     black. It is a warm near-black (rgb 16,11,12); forcing #000 would leave a
 *     visible lighter square in a tab.
 *
 *   - The UI mark is a MASK, not a picture, which is the same trick the admin
 *     already uses. CSS cannot recolour the raster, and its opaque black square
 *     would show as a box on anything lighter. So the shipped mask is white with
 *     the source's luminance in its alpha: painting a gradient through
 *     mask-image reproduces the drawing exactly - every hairline survives,
 *     because the alpha carries the artwork's own brightness - while letting the
 *     colour come from CSS. One asset, follows the accent, sharp at any size.
 *
 *   - That mask is also why the lion is legible at 32px here, which a tight
 *     crop of the face is not. Measured: the full head reads at 48 and 32, while
 *     cropping into the face loses the head silhouette and degrades into
 *     abstract hatching. So there is one crop, not two - the full head.
 *
 *   - The favicon is quantised to a 16-colour palette above 32px. The mark is
 *     thin neon lines over near-black plus a lot of glitch texture, which
 *     truecolour PNG compresses badly: the first build came out at 124KB, five
 *     times the placeholder it replaced, for the same tab. Sixteen colours is
 *     enough because the mark is two-tone, and it brings the file back under the
 *     placeholder's 25KB.
 *
 * sharp is a dependency of next, so it is present on every install. It is
 * declared in devDependencies anyway: this runs in a prebuild hook, and an
 * undeclared package turning up missing should not be able to break the build.
 */

import { writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import sharp from 'sharp';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const SOURCE = resolve(root, '..', 'mobile/assets/icon.png');
const FAVICON = resolve(root, 'src/app/favicon.ico');
const APPLE_ICON = resolve(root, 'src/app/apple-icon.png');
const MASK = resolve(root, 'public/lion-mask.png');

/** Tab strips ask for these; 256 covers high-DPI and the Windows tile. */
const ICO_SIZES = [16, 32, 48, 256];
const APPLE_SIZE = 180;
/** The mask is drawn at this size; CSS scales it. The largest mark the site uses
 * is 56 CSS px, so 256 covers a 4.5x display with room to spare while staying
 * lighter than the admin's own 320px copy of the same mask. */
const MASK_SIZE = 256;
/** Breathing room around the lion, as a fraction of its bounding box. */
const MARGIN = 0.05;

/**
 * Find the lion by comparing every pixel to the backdrop sampled from the image
 * border. Derived rather than hardcoded so a re-export of the asset at another
 * size, or with different padding, still produces the same crop.
 */
async function measure(path) {
  const { data, info } = await sharp(path).removeAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width: W, height: H, channels: C } = info;
  const at = (x, y, c) => data[(y * W + x) * C + c];

  const border = [];
  for (let x = 0; x < W; x += Math.max(1, W >> 7)) border.push([x, 0], [x, H - 1]);
  for (let y = 0; y < H; y += Math.max(1, H >> 7)) border.push([0, y], [W - 1, y]);
  const backdrop = [0, 1, 2].map((c) => {
    const sorted = border.map(([x, y]) => at(x, y, c)).sort((p, q) => p - q);
    return sorted[sorted.length >> 1];
  });

  let minX = W, minY = H, maxX = -1, maxY = -1;
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const off = Math.max(
        Math.abs(at(x, y, 0) - backdrop[0]),
        Math.abs(at(x, y, 1) - backdrop[1]),
        Math.abs(at(x, y, 2) - backdrop[2]),
      );
      if (off <= 18) continue;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
    }
  }
  if (maxX < 0) throw new Error(`could not find the lion in ${SOURCE} - nothing differs from the backdrop`);

  const w = maxX - minX + 1;
  const h = maxY - minY + 1;
  if (w < W * 0.05 || h < H * 0.05) {
    throw new Error(`lion bounding box ${w}x${h} is implausibly small in a ${W}x${H} image - refusing to guess`);
  }
  return { W, H, backdrop, box: { minX, minY, w, h } };
}

/** Centre the lion's square bounding box in the image and pad it. */
function cropFor({ W, H, box }) {
  const side = Math.max(box.w, box.h);
  const padded = Math.round(side * (1 + MARGIN * 2));
  const left = Math.round(box.minX - (padded - side) / 2);
  const top = Math.round(box.minY - (padded - side) / 2);
  const size = Math.min(padded, W, H);
  return {
    left: Math.max(0, Math.min(left, W - size)),
    top: Math.max(0, Math.min(top, H - size)),
    size,
  };
}

/**
 * Pack PNG frames into an ICO. PNG-in-ICO is what every current browser reads,
 * and it is the only way sharp can help here: it writes PNG, not BMP/DIB, and a
 * 16x16 BMP encoder is not worth the code.
 */
function packIco(frames) {
  const dir = Buffer.alloc(6 + 16 * frames.length);
  dir.writeUInt16LE(0, 0);
  dir.writeUInt16LE(1, 2);
  dir.writeUInt16LE(frames.length, 4);

  let offset = dir.length;
  frames.forEach((frame, i) => {
    const o = 6 + i * 16;
    dir[o] = frame.size >= 256 ? 0 : frame.size;
    dir[o + 1] = frame.size >= 256 ? 0 : frame.size;
    dir.writeUInt16LE(1, o + 4);
    dir.writeUInt16LE(32, o + 6);
    dir.writeUInt32LE(frame.data.length, o + 8);
    dir.writeUInt32LE(offset, o + 12);
    offset += frame.data.length;
  });

  return Buffer.concat([dir, ...frames.map((f) => f.data)]);
}

const m = await measure(SOURCE);
const crop = cropFor(m);
const backdrop = { r: m.backdrop[0], g: m.backdrop[1], b: m.backdrop[2] };

/**
 * @param rounded rounds the corners and lets the tab background through. Tab icon
 *   only: browsers do not mask favicons, so a hard black square reads as
 *   unfinished. The home-screen icon is deliberately NOT rounded - iOS applies
 *   its own squircle mask, and rounding here as well would show its background
 *   through the corners it cuts.
 */
const renderIcon = async (size, { palette = false, rounded = false } = {}) => {
  let pipe = sharp(SOURCE)
    .extract({ left: crop.left, top: crop.top, width: crop.size, height: crop.size })
    /* Flatten onto the sampled backdrop: the source has no alpha, and a tab can
     * be light, so the mark must not depend on what is behind it. */
    .flatten({ background: backdrop })
    .resize(size, size, { fit: 'fill', kernel: 'lanczos3' });

  if (rounded) {
    const r = Math.round(size * 0.22);
    pipe = sharp(await pipe.png().toBuffer()).composite([
      {
        input: Buffer.from(
          `<svg width="${size}" height="${size}"><rect width="${size}" height="${size}" rx="${r}" fill="#fff"/></svg>`,
        ),
        blend: 'dest-in',
      },
    ]);
  }

  return pipe.png(
    palette
      ? { compressionLevel: 9, palette: true, colours: 16, effort: 10, dither: 0 }
      : { compressionLevel: 9 },
  );
};

/**
 * White RGB with the drawing's luminance in the alpha. Resized before the alpha
 * is taken, so lanczos softens the thin lines instead of aliasing them into
 * sparkle, and the backdrop is subtracted so the lion's own black square becomes
 * fully transparent rather than a faint grey haze behind the mark.
 */
async function renderMask() {
  const lum = await sharp(SOURCE)
    .extract({ left: crop.left, top: crop.top, width: crop.size, height: crop.size })
    .resize(MASK_SIZE, MASK_SIZE, { fit: 'fill', kernel: 'lanczos3' })
    .greyscale()
    .raw()
    .toBuffer();

  const floor = 0.2126 * backdrop.r + 0.7152 * backdrop.g + 0.0722 * backdrop.b;
  const range = 255 - floor;
  const rgba = Buffer.alloc(MASK_SIZE * MASK_SIZE * 4);
  for (let i = 0; i < MASK_SIZE * MASK_SIZE; i++) {
    const a = Math.max(0, Math.min(1, (lum[i] - floor) / range));
    const o = i * 4;
    rgba[o] = 255;
    rgba[o + 1] = 255;
    rgba[o + 2] = 255;
    rgba[o + 3] = Math.round(a * 255);
  }
  return sharp(rgba, { raw: { width: MASK_SIZE, height: MASK_SIZE, channels: 4 } })
    .png({ compressionLevel: 9 })
    .toBuffer();
}

const frames = [];
for (const size of ICO_SIZES) {
  frames.push({ size, data: await (await renderIcon(size, { palette: size >= 48, rounded: true })).toBuffer() });
}
await writeFile(FAVICON, packIco(frames));
await writeFile(APPLE_ICON, await (await renderIcon(APPLE_SIZE, { palette: true })).toBuffer());
await writeFile(MASK, await renderMask());

console.log(
  `[brand] lion at ${m.box.w}x${m.box.h} -> cropped ${crop.size}px, backdrop rgb(${backdrop.r},${backdrop.g},${backdrop.b}); ` +
  `wrote favicon.ico (${ICO_SIZES.join('/')}), apple-icon.png (${APPLE_SIZE}), lion-mask.png (${MASK_SIZE})`,
);