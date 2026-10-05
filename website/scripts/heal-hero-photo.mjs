#!/usr/bin/env node
/**
 * Produce public/hero-clean.jpg from public/hero-new.jpg.
 *
 * hero-new.jpg is an AI-generated marketing mockup, so its headline, subhead,
 * body copy and green CTA are composited into the pixels. Using it as the hero
 * BACKGROUND puts a finished advert underneath the live HTML headline, which
 * says the same things. At desktop widths the live copy happened to cover it; at
 * 502px `background-size: cover` re-cropped the photo, the baked copy slid out
 * from under the live headline, and its tail - the "ВО" of "ПАЛИВО" - painted
 * over the App Store badge.
 *
 * There is no text-free version of this render in the repo, so the copy is
 * inpainted rather than regenerated: for every row of the box, the fill colour
 * is the median of a clean strip just outside each vertical edge, blended
 * horizontally so the patch is continuous on both sides. The vertical blur is
 * load-bearing - row medians are noisy, and the horizontal stretch turns any
 * row-to-row jitter into visible banding.
 *
 * Run from the website/ directory:  node scripts/heal-hero-photo.mjs
 * The box is measured, not guessed; scripts/check-hero.mjs fails if any
 * text-like run is left behind, so a wrong box cannot ship quietly.
 */

import sharp from 'sharp';

const SRC = 'public/hero-new.jpg';
const OUT = 'public/hero-clean.jpg';

/**
 * Bounding box of the baked copy, measured by scanning the source for bright,
 * low-saturation rows in the left 60% of the frame: the copy occupies
 * x 56..548, y 305..555. Padded outward so the feather has room to blend.
 */
const X0 = 40;
const X1 = 566;
const Y0 = 288;
const Y1 = 572;
const FEATHER = 20;

const { data, info } = await sharp(SRC).removeAlpha().raw().toBuffer({ resolveWithObject: true });
const { width: W, height: H, channels: C } = info;
const at = (x, y, c) => data[(y * W + x) * C + c];

const median = (xs, y, c) => {
  const a = xs.map((x) => at(x, y, c)).sort((p, q) => p - q);
  return a[a.length >> 1];
};

const smooth = (t) => t * t * (3 - 2 * t);
const ramp = (d) => Math.max(0, Math.min(1, d / FEATHER));

// Median colour of the strip just outside each vertical edge, per row.
const LEFT = [], RIGHT = [];
const leftXs = [], rightXs = [];
for (let x = 6; x < 36; x++) leftXs.push(x);
for (let x = X1 + FEATHER; x < X1 + FEATHER + 30 && x < W; x++) rightXs.push(x);

for (let y = 0; y < H; y++) {
  LEFT[y] = [0, 1, 2].map((c) => median(leftXs, y, c));
  RIGHT[y] = [0, 1, 2].map((c) => median(rightXs, y, c));
}

// Row medians are noisy; the patch stretches them sideways, which turns any
// row-to-row jitter into visible banding. Smooth them vertically first.
const boxBlur = (rows, radius, passes) => {
  for (let p = 0; p < passes; p++) {
    for (let c = 0; c < 3; c++) {
      const src = rows.map((r) => r[c]);
      for (let y = 0; y < H; y++) {
        let sum = 0, n = 0;
        for (let y2 = Math.max(0, y - radius); y2 <= Math.min(H - 1, y + radius); y2++) {
          sum += src[y2];
          n++;
        }
        rows[y][c] = sum / n;
      }
    }
  }
};
boxBlur(LEFT, 40, 3);
boxBlur(RIGHT, 40, 3);

let patched = 0;
for (let y = Y0; y <= Y1; y++) {
  const a = ramp(Math.min(y - Y0, Y1 - y));
  for (let x = X0; x <= X1; x++) {
    const t = smooth((x - X0) / (X1 - X0));
    const alpha = a * ramp(Math.min(x - X0, X1 - x));
    if (alpha <= 0) continue;
    const i = (y * W + x) * C;
    for (let c = 0; c < 3; c++) {
      const fill = LEFT[y][c] + (RIGHT[y][c] - LEFT[y][c]) * t;
      data[i + c] = Math.round(data[i + c] * (1 - alpha) + fill * alpha);
    }
    patched++;
  }
}

await sharp(data, { raw: { width: W, height: H, channels: C } })
  .jpeg({ quality: 88, mozjpeg: true, chromaSubsampling: '4:4:4' })
  .toFile(OUT);

console.log(`healed ${patched}px -> ${OUT}`);
