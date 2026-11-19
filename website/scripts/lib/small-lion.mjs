/**
 * The small cut: the lion redrawn for slots at or below 32 CSS px.
 *
 * Why a second artwork exists at all (planning #852). The lion in
 * mobile/assets/icon.png is a drawing of hairlines: measured on the shipped
 * 256px mask, the mean lit run is 4.16px and the median is 2px, which is 1.63%
 * and 0.78% of the mark's own box. `mask-size: contain` maps the box onto the
 * slot, so at 32 CSS px the median stroke arrives 0.25 device px wide and at 16
 * it is 0.125. A stroke under a pixel does not render as a line - it renders as a
 * wash whose shape the resampling filter decides, and the detail that makes the
 * drawing a lion (which way the muzzle points, where the jaw opens) is exactly
 * the detail those strokes carry. On the tab frame that measured **95.8% of the
 * drawn pixels as neither the line nor the background**.
 *
 * Switching to a vector cut of the SAME drawing does not fix it, and that was
 * measured too: the raster glow is what bridges the sub-pixel gaps and keeps the
 * mark reading at 56px, so a vector trace of it is worse there, not better. What a
 * small slot needs is fewer, bigger features - a cut drawn for the small end.
 *
 * THE GEOMETRY, in a 100-unit box with y downwards, muzzle left:
 *
 *   mane    a scalloped ellipse, 7 locks, swept back towards the tail. Seven,
 *           not the artwork's hairline fringe: at 16px there is canvas for
 *           roughly that many, and the count is what reads as a mane rather
 *           than as a fringe. The locks are also why the outline is not a
 *           circle - circles read as badges.
 *   ear     one spike at the top, the tallest, so the silhouette has a
 *           recognisable bump and not a ring of equal teeth.
 *   jaws    two wedges that both reach the left edge - the muzzle above, the
 *           chin below - with the open mouth as the gap between them. The gap
 *           is a feature of the OUTER silhouette, not an interior line: it is
 *           the only mouth that survives having its interior detail deleted.
 *
 * Every number is a whole-number share of the box rather than an absolute pixel,
 * so the same cut renders at 16 and at 32 without a second drawing. Measured on
 * the rendered frames (see scripts/lib/legibility.mjs): mud falls from 86.9% to
 * 39.4% at 16px and from 84.4% to 21.3% at 32px.
 *
 * WHY IT IS RASTERISED IN THIS FILE RATHER THAN BY librsvg
 *
 * The first version of this cut was an SVG handed to sharp, and it passed every
 * local check and then failed CI with two files of exactly the same byte LENGTH
 * and different bytes: `check-brand-assets.mjs` derives the frames on the CI
 * runner and compares them with the committed ones, and an SVG goes through
 * librsvg's own float pipeline and palette path, which is not promised to be
 * bit-identical across builds of the library. The old favicon was stable because
 * it was a pure image resample; an SVG is not that.
 *
 * So the shapes stay as numbers here and the pixels are computed here: the union
 * of a few polygons, sampled 4x4 per device pixel, filled with the gradient and
 * flattened onto the backdrop. Same input, same bytes, on any machine - which is
 * the only property that makes a committed binary gateable at all.
 */

/** The slot above which the full artwork still reads. Both sizes come from this
 * cut; 48 and 256 stay the drawn lion, because at 48 the mask's own glow starts
 * doing the work again and the hairline detail is worth having. */
export const SMALL_ICO_SIZES = [16, 32];

/** The mark's gradient: the same white-into-lime the painted `.brand-mark`
 * surface uses, so the tab and the mark are one brand at every size. */
const GRADIENT = {
  stops: [
    { at: 0, rgb: [255, 255, 255] },
    { at: 0.52, rgb: [197, 255, 0] },
    { at: 1, rgb: [197, 255, 0] },
  ],
  /** CSS `linear-gradient(155deg, ...)`: 155 degrees clockwise from "to top". */
  angle: 155,
};

/** Samples per axis, per device pixel. 4x4 takes the staircase off a 16px edge
 * without turning the locks into mush. */
const SUPERSAMPLE = 4;

/**
 * The mane outline: a scalloped ellipse. The sawtooth is what makes the locks
 * read as swept rather than as a flower - the radius climbs slowly across each
 * lock and drops sharply at its back edge, and the shear drifts the pattern
 * backwards as the angle advances.
 */
function mane({ cx = 58, cy = 48, rx = 26, ry = 30, locks = 7, from = -110, to = 135, amp = 0.45, shear = 0.3 } = {}) {
  const points = [];
  for (let a = from; a <= to; a += 1) {
    const t = (a - from) / (to - from);
    const w = Math.sin(Math.PI * t) ** 0.7; // no locks across the face side
    const u = (locks * (t + shear * t * t)) % 1;
    const k = 1 + amp * w * (u ** 0.85 - 0.35);
    const rad = (a * Math.PI) / 180;
    points.push([cx + rx * k * Math.cos(rad), cy + ry * k * Math.sin(rad)]);
  }
  return points;
}

/** The ellipse the mane is cut from, as a polygon: one shape list, one test. */
function ellipsePoints(cx, cy, rx, ry, segments = 96) {
  const points = [];
  for (let i = 0; i < segments; i++) {
    const a = (i / segments) * Math.PI * 2;
    points.push([cx + rx * Math.cos(a), cy + ry * Math.sin(a)]);
  }
  return points;
}

/** Every filled shape of the cut, in unit space. */
function shapes() {
  return [
    mane(),
    ellipsePoints(58, 48, 26, 30),
    // upper jaw: the muzzle, reaching the left edge
    [
      [56, 30],
      [6, 39],
      [6, 49],
      [54, 45],
    ],
    // lower jaw: the chin, below the mouth
    [
      [56, 63],
      [14, 61],
      [14, 70],
      [54, 70],
    ],
    // ear: the one tall spike
    [
      [46, 24],
      [50, 2],
      [64, 20],
    ],
  ];
}

/** Ray casting, so a concave outline (the mane) is as valid as a triangle. */
function inPolygon(polygon, x, y) {
  let inside = false;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
    const [xi, yi] = polygon[i];
    const [xj, yj] = polygon[j];
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

const lerp = (a, b, t) => a + (b - a) * t;

/** The gradient's colour at a point, in unit space. */
function gradientAt(x, y) {
  const rad = (GRADIENT.angle * Math.PI) / 180;
  // "to top" rotated 155 degrees clockwise, then normalised onto the box diagonal
  const dx = Math.sin(rad);
  const dy = -Math.cos(rad);
  const t = Math.min(1, Math.max(0, ((x / 100) * dx + (y / 100) * dy + Math.abs(dx) / 2 + Math.abs(dy) / 2) / (Math.abs(dx) + Math.abs(dy))));
  const { stops } = GRADIENT;
  for (let i = 1; i < stops.length; i++) {
    if (t <= stops[i].at) {
      const span = stops[i].at - stops[i - 1].at;
      const local = span === 0 ? 0 : (t - stops[i - 1].at) / span;
      return stops[i - 1].rgb.map((c, k) => lerp(c, stops[i].rgb[k], local));
    }
  }
  return stops[stops.length - 1].rgb;
}

/**
 * The frames of the cut, as raw RGBA at exactly the requested size.
 *
 * @param {number} size square, in pixels
 * @param {{r:number,g:number,b:number}} backdrop the surface it is flattened onto
 * @returns {Buffer} RGBA, `size * size * 4` bytes
 */
export function renderSmallCut(size, backdrop) {
  const polygons = shapes();
  const out = Buffer.alloc(size * size * 4);
  const radius = size * 0.22;

  for (let py = 0; py < size; py++) {
    for (let px = 0; px < size; px++) {
      let covered = 0;
      let r = 0;
      let g = 0;
      let b = 0;
      for (let sy = 0; sy < SUPERSAMPLE; sy++) {
        for (let sx = 0; sx < SUPERSAMPLE; sx++) {
          // sample centre in unit space
          const ux = ((px + (sx + 0.5) / SUPERSAMPLE) / size) * 100;
          const uy = ((py + (sy + 0.5) / SUPERSAMPLE) / size) * 100;
          if (!polygons.some((p) => inPolygon(p, ux, uy))) continue;
          covered++;
          const [cr, cg, cb] = gradientAt(ux, uy);
          r += cr;
          g += cg;
          b += cb;
        }
      }
      if (!covered) continue;

      // the tab icon's rounded corners, as coverage rather than a second pass
      const cx = px + 0.5;
      const cy = py + 0.5;
      const qx = Math.max(radius - cx, 0, cx - (size - radius));
      const qy = Math.max(radius - cy, 0, cy - (size - radius));
      const cornerAlpha = Math.hypot(qx, qy) <= radius ? 1 : 0;

      const alpha = (covered / (SUPERSAMPLE * SUPERSAMPLE)) * cornerAlpha;
      const o = (py * size + px) * 4;
      out[o] = Math.round((r / covered) * alpha + backdrop.r * (1 - alpha));
      out[o + 1] = Math.round((g / covered) * alpha + backdrop.g * (1 - alpha));
      out[o + 2] = Math.round((b / covered) * alpha + backdrop.b * (1 - alpha));
      out[o + 3] = 255;
    }
  }

  return out;
}
