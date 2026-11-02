/**
 * The FuelFlow brand surfaces, derived from one asset: mobile/assets/icon.png.
 *
 * Exported as a module so the generator and the CI check cannot drift apart -
 * the check has to reproduce these bytes exactly, and a second implementation
 * would quietly stop doing that.
 *
 *   favicon.ico        16/32/48/256 PNG frames, rounded corners
 *   apple-icon.png     180px, square - iOS applies its own mask
 *   lion-mask.png      the UI mark: white, alpha = the drawing's own luminance
 *   adaptive-icon.png  the Android launcher foreground, inside the safe zone
 *
 * The tab icon and the mask are each rendered once and written to both web
 * surfaces, because two renders of one lion are two chances for one of them to
 * quietly stop being the lion. Both had drifted: the admin's tab icon was a
 * 1024px JPEG named .png, and its copy of the mask was a hand-made 320px render
 * with a steeper alpha ramp and no glow, so the same mark painted differently in
 * the admin and on the site. Neither was reachable from any gate.
 *
 * Three details that are easy to get wrong:
 *
 * The crop is measured, not hardcoded. The source is 1024x1024 with the lion
 * sitting inside it, so a plain downscale ships an icon that is 70% empty. The
 * bounding box is found by comparing every pixel to the backdrop sampled from
 * the image border, so a re-export at another size or with different padding
 * still produces the same crop, and a plausibility check refuses to guess if the
 * measurement looks wrong.
 *
 * The backdrop is sampled, not forced to black. It is a warm near-black
 * (rgb 16,11,12); forcing #000 would leave a visible lighter square in a tab.
 *
 * The UI mark is a MASK, not a picture - the same trick the admin already uses.
 * CSS cannot recolour the raster and its opaque black square would show as a box
 * on anything lighter, so the mask is white with the source's luminance in its
 * alpha. Painting a gradient through mask-image reproduces the drawing exactly,
 * every hairline surviving, because the alpha carries the artwork's own
 * brightness, while the colour comes from CSS. One asset, follows the accent,
 * sharp at any size.
 *
 * That mask is also why the lion is legible at 32px here, which a tight crop of
 * the face is not. Measured: the full head reads at 48 and 32, while cropping
 * into the face loses the head silhouette and degrades into abstract hatching.
 * So there is one crop, not two.
 *
 * The favicon is quantised to a 16-colour palette above 32px. The mark is thin
 * neon lines over near-black plus a lot of glitch texture, which truecolour PNG
 * compresses badly: the first build of this icon came out at 124KB, five times
 * the placeholder it replaced, for the same tab. Sixteen colours is enough
 * because the mark is two-tone, and it brings the whole file back under the
 * placeholder's 25KB. The Android foreground is NOT quantised - it is the full
 * 1024 canvas, where 16 colours would band the glow, and it is a build-time
 * resource rather than something a browser downloads.
 *
 * The Android foreground is scaled down, not cropped. An adaptive icon is a
 * 108dp layer that the launcher masks - usually to a circle, after scaling the
 * layer by 1.5 - so only the central 66dp is guaranteed to survive. The lion is
 * 719px of its 1024 source, so at 70.2% it sat outside that 61.1% safe zone and
 * the launcher trimmed the outermost mane. Now the padded crop is drawn at the
 * safe fraction on a canvas of the sampled backdrop, which puts the lion itself
 * at 55.6% - 21% smaller than before, and back inside every mask shape Google
 * publishes.
 */

import sharp from 'sharp';

/** Tab strips ask for these; 256 covers high-DPI and the Windows tile. */
export const ICO_SIZES = [16, 32, 48, 256];
export const APPLE_ICON_SIZE = 180;
/** The mask is drawn at this size; CSS scales it. The largest mark either web
 * surface uses is 76 CSS px (the admin login), so 256 covers a 3x display with
 * room to spare while staying lighter than the hand-made 320px copy it replaced. */
export const MASK_SIZE = 256;
/** Same canvas as the source, so the foreground is not downscaled relative to
 * the square iOS icon sitting next to it in mobile/assets. */
export const ADAPTIVE_CANVAS = 1024;
/** Google's adaptive-icon safe zone: the inner 66dp of the 108dp layer. */
export const ADAPTIVE_SAFE_FRACTION = 66 / 108;
/** Breathing room around the lion, as a fraction of its bounding box. */
const MARGIN = 0.05;

/**
 * Find the lion by comparing every pixel to the backdrop sampled from the image
 * border.
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
  if (maxX < 0) throw new Error('nothing in the source differs from its backdrop, so there is no lion to find');

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

/**
 * @param rounded rounds the corners and lets the tab background through. Tab icon
 *   only: browsers do not mask favicons, so a hard black square reads as
 *   unfinished. The home-screen icon is deliberately NOT rounded - iOS applies
 *   its own squircle mask, and rounding here as well would show its background
 *   through the corners it cuts.
 */
const renderIcon = async (source, crop, backdrop, size, { palette = false, rounded = false } = {}) => {
  let pipe = sharp(source)
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

  return pipe
    .png(
      palette
        ? { compressionLevel: 9, palette: true, colours: 16, effort: 10, dither: 0 }
        : { compressionLevel: 9 },
    )
    .toBuffer();
};

/**
 * White RGB with the drawing's luminance in the alpha. Resized before the alpha
 * is taken, so lanczos softens the thin lines instead of aliasing them into
 * sparkle, and the backdrop is subtracted so the lion's own black square becomes
 * fully transparent rather than a faint grey haze behind the mark.
 */
async function renderMask(source, crop, backdrop) {
  const lum = await sharp(source)
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

/**
 * The Android launcher foreground: the lion inside the safe zone, on a canvas of
 * its own backdrop.
 *
 * Flattened rather than keyed to transparency. The artwork has no alpha and its
 * dark interior is the same colour as the background it sits on, so chroma-keying
 * would eat part of the mane. Covering the whole layer with the sampled backdrop
 * makes app.json's own background colour unreachable, exactly as the full-bleed
 * square it replaces does - and the mask the launcher applies now has nothing
 * left to trim.
 */
async function renderAdaptiveIcon(source, crop, backdrop) {
  const content = Math.round(ADAPTIVE_CANVAS * ADAPTIVE_SAFE_FRACTION);
  const offset = Math.round((ADAPTIVE_CANVAS - content) / 2);
  return sharp({
    create: { width: ADAPTIVE_CANVAS, height: ADAPTIVE_CANVAS, channels: 3, background: backdrop },
  })
    .composite([{ input: await renderIcon(source, crop, backdrop, content), left: offset, top: offset }])
    /* Composite works in RGBA and the result is fully opaque, so the alpha is
     * dropped to keep the same format the square iOS icon next to it has. */
    .removeAlpha()
    .png({ compressionLevel: 9 })
    .toBuffer();
}

/**
 * Build every brand surface from the lion. Pure: it reads one file and returns
 * buffers, and touches nothing on disk. That is what lets the CI check compare
 * its output against the committed assets byte for byte.
 *
 * @param source path to the lion PNG
 */
export async function buildBrandAssets(source) {
  const m = await measure(source);
  const crop = cropFor(m);
  const backdrop = { r: m.backdrop[0], g: m.backdrop[1], b: m.backdrop[2] };

  const frames = [];
  for (const size of ICO_SIZES) {
    frames.push({ size, data: await renderIcon(source, crop, backdrop, size, { palette: size >= 48, rounded: true }) });
  }

  return {
    favicon: packIco(frames),
    appleIcon: await renderIcon(source, crop, backdrop, APPLE_ICON_SIZE, { palette: true }),
    mask: await renderMask(source, crop, backdrop),
    adaptiveIcon: await renderAdaptiveIcon(source, crop, backdrop),
    meta: {
      lion: `${m.box.w}x${m.box.h}`,
      cropped: crop.size,
      backdrop: `rgb(${backdrop.r},${backdrop.g},${backdrop.b})`,
    },
  };
}