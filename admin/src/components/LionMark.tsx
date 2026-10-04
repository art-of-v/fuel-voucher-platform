interface LionMarkProps {
    /** Rendered edge length in px. The mask is 320px, so anything up to ~120px stays crisp. */
    size?: number;
    className?: string;
}

/**
 * The FuelFlow lion, painted by the active theme.
 *
 * `mobile/assets/icon.png` is the only lion in the repo: a neon-green raster on an
 * opaque black square. Two things follow from that. CSS cannot recolour it, and the
 * black would show as a box on any lighter surface — so the admin ships
 * `src/assets/lion-mask.png` instead: a white silhouette whose *alpha* is the
 * source's own brightness.
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