interface LionMarkProps {
  /** Rendered edge length in px. The mask is 256px, so 85 CSS px is still 1:1 at
   * a 3x display; the two places that use it ask for 56 and 76. */
  size?: number;
  className?: string;
}

/**
 * The FuelFlow lion, painted by the active theme.
 *
 * `mobile/assets/icon.png` is the source, and every other lion in this repository
 * is derived from it by `website/scripts/generate-brand-assets.mjs` - including
 * the `src/assets/lion-mask.png` this component paints, which
 * `check-brand-assets.mjs` proves is those bytes rather than a hand-made copy.
 * Two things follow from that source: CSS cannot recolour a raster, and its
 * opaque black square would show as a box on any lighter theme - so what ships
 * here is a mask, not a picture: white, whose *alpha* is the source's own
 * brightness.
 *
 * A mask is the whole trick. Painting a gradient through `mask-image` reproduces
 * the original drawing exactly — every hairline survives, because the alpha
 * carries the artwork's luminance — while letting the fill come from the theme.
 * So one asset serves all twelve themes, follows a hue change with no
 * regeneration, and stays sharp at any size.
 *
 * The alternative, baking a separate coloured PNG per theme, is what produced the
 * grey smudge this replaced: one asset per theme, and a hue edit away from broken.
 */
export function LionMark({ size = 56, className }: LionMarkProps) {
  return (
    <span
      role="presentation"
      aria-hidden="true"
      style={{ width: size, height: size }}
      className={className ? `brand-mark ${className}` : "brand-mark"}
    />
  );
}
