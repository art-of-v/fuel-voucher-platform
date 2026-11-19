/**
 * How legible a mark is at a given size - as numbers, not as an opinion.
 *
 * Planning #852 measured the shipped lion this way: at 16px, 95.8% of the drawn
 * pixels were not lines but values between the line and the background, and that
 * is what "a dark smudge" means in arithmetic. The same two numbers decide
 * whether a replacement is better, so they live here rather than in a review
 * thread:
 *
 *   stroke  how wide the drawn lines are, as a share of the mark's own box.
 *           A stroke below one device pixel (1/16 = 6.25% at 16px, 1/32 = 3.1%
 *           at 32px) does not render as a line at all - it renders as a wash
 *           whose shape the resampling filter chooses.
 *   mud     the share of DRAWN pixels that are neither the line nor the
 *           background. This is the smudge, and it is measured against the drawn
 *           pixels rather than the frame so a mark that covers less of the box is
 *           not punished for being small.
 *
 * Both are computed from the rendered mark on the backdrop it will actually sit
 * on, because a mark that is legible on white is not necessarily legible on
 * rgb(16,11,12) - and the tab, the footer and the admin login are all dark.
 *
 * The luminance conversion is Rec. 709 (0.2126/0.7152/0.0722), the same weights
 * the mask renderer uses to turn the artwork's brightness into alpha, so the
 * three numbers here describe the same drawing the surfaces paint.
 *
 * "Fully lit" is measured against the mark's OWN brightest ink, not against
 * white: the lion is lime, and its stroke tops out at about 88% of white, so a
 * white-relative threshold would report a perfectly crisp lime mark as mud. The
 * reference is the 97th percentile of the rendered luminance, which is the
 * stroke colour for a two-tone mark and is not moved by a handful of pixels.
 */

/** Fully lit: at or above this share of the way from the backdrop to the ink. */
export const LIT = 0.85;
/** Backdrop: at or below this share of the way from the backdrop to the ink. */
export const BACKDROP = 0.1;
/** The percentile of rendered luminance that counts as the mark's own ink. */
export const INK_PERCENTILE = 0.97;

const lum = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

/**
 * @param {Buffer} pixels raw pixels of the rendered mark, already flattened onto
 *   the backdrop - RGB or RGBA, both are read
 * @param {number} width
 * @param {number} height
 * @param {{r:number,g:number,b:number}} backdrop
 * @param {number} channels channels per pixel in `pixels` (3 or 4)
 */
export function measureLegibility(pixels, width, height, backdrop, channels = 4) {
  const floor = lum(backdrop.r, backdrop.g, backdrop.b);
  const values = new Float64Array(width * height);
  for (let i = 0; i < width * height; i++) {
    const o = i * channels;
    values[i] = lum(pixels[o], pixels[o + 1], pixels[o + 2]);
  }
  const sortedValues = Float64Array.from(values).sort();
  const ink = sortedValues[Math.min(sortedValues.length - 1, Math.floor(sortedValues.length * INK_PERCENTILE))];
  const range = Math.max(1, ink - floor);

  let lit = 0;
  let mud = 0;
  const runs = [];
  for (let y = 0; y < height; y++) {
    let run = 0;
    for (let x = 0; x < width; x++) {
      const v = (values[y * width + x] - floor) / range;
      if (v >= LIT) {
        lit++;
        run++;
      } else {
        if (v > BACKDROP) mud++;
        if (run) runs.push(run);
        run = 0;
      }
    }
    if (run) runs.push(run);
  }

  const drawn = lit + mud;
  const strokePct = runs.length ? ((runs.reduce((a, b) => a + b, 0) / runs.length / width) * 100) : 0;
  const sorted = [...runs].sort((a, b) => a - b);
  const medianPct = sorted.length ? ((sorted[sorted.length >> 1] / width) * 100) : 0;

  return {
    lit,
    mud,
    drawn,
    ink,
    mudPct: drawn ? (mud / drawn) * 100 : 0,
    strokePct,
    medianStrokePct: medianPct,
    runs: runs.length,
  };
}

/**
 * The floor a small cut has to clear, per rendered size: a stroke has to land on
 * at least one whole device pixel at 1x.
 */
export const strokeFloorPct = (size) => (100 / size);

/**
 * The open jaw, in whole pixels.
 *
 * At 16px the mouth is not a hole inside the mark: the two jaws reach the left
 * edge and the gap between them reaches it too, so the notch shows up as a band
 * of rows whose leftmost lit pixel sits further right than the rows above and
 * below it. The measure is the DEPTH of that retreat - how many pixels the
 * outline backs off at the mouth - taken against the nearest rows on either side
 * rather than against the whole frame, because the mark's own muzzle and chin are
 * at different depths and a global minimum would compare them to each other.
 *
 * This is the one piece of the lion the silhouette has to keep: a mark whose
 * mouth closed in the resize is a mark with no jaw, and on the shipped 16px frame
 * it measured 0px.
 *
 * @returns the depth of the deepest retreat, in pixels (0 when there is none)
 */
export function jawNotchPx(pixels, width, height, backdrop, channels = 4, litFloor = 0.45) {
  const floor = lum(backdrop.r, backdrop.g, backdrop.b);
  const ink = 255 - floor;
  const leftmost = [];
  for (let y = 0; y < height; y++) {
    leftmost[y] = -1;
    for (let x = 0; x < width; x++) {
      const o = (y * width + x) * channels;
      const v = (lum(pixels[o], pixels[o + 1], pixels[o + 2]) - floor) / Math.max(1, ink);
      if (v >= litFloor) {
        leftmost[y] = x;
        break;
      }
    }
  }

  const WINDOW = 3;
  let best = 0;
  for (let y = 0; y < height; y++) {
    if (!(leftmost[y] >= 0)) continue;
    let nearest = Infinity;
    for (let d = -WINDOW; d <= WINDOW; d++) {
      if (d === 0) continue;
      const other = leftmost[y + d];
      if (other >= 0) nearest = Math.min(nearest, other);
    }
    if (nearest < Infinity) best = Math.max(best, leftmost[y] - nearest);
  }
  return best;
}
