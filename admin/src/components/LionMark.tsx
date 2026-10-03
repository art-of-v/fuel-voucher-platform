import { cn } from "@/lib/utils";
import lion from "@/assets/lion.png";

interface LionMarkProps {
    /** Rendered height in px. The mark is 1:1, so width follows. */
    size?: number;
    className?: string;
}

/**
 * The FuelFlow lion, recoloured to mercury chrome, for the mercury theme only.
 *
 * `mobile/assets/icon.png` is the only lion in the repo — a 1024px neon-green
 * raster on an opaque black square, 1.2 MB. CSS cannot recolour it and the black
 * would show as a box on any lighter surface, so the admin ships a pre-silvered
 * copy instead (src/assets/lion.png, 256px RGBA, 22 KB).
 *
 * To regenerate it from the mobile original (Pillow):
 *   1. mask = max(R, G, B) of the source — max, not the green channel, or the dim
 *      blue circuitry in the mane clips away;
 *   2. crush values <= 14 to 0, then gamma the rest up, so the hairline work
 *      survives the downscale instead of dissolving into grey;
 *   3. crop to the bbox of mask >= 48 — the source has a faint green haze in the
 *      corners, so the raw bbox is the whole frame and the mark sits small in
 *      dead space;
 *   4. resize to 256 with LANCZOS, then unsharp-mask to re-solidify the 1px
 *      strokes the resample softened;
 *   5. colour = colorize(mask, black #5c6773, mid #9aa6b3, white #fbfdff), so the
 *      colour follows the mark's own luminance and the highlights read as polish;
 *   6. multiply by a diagonal sheen built from two blended axis gradients (a
 *      rotated linear_gradient seams where it wraps);
 *   7. alpha = mask, which is what removes the black backdrop;
 *   8. quantise to 96 RGBA colours — at 40-64px none of that detail survives, and
 *      it takes the file from 217 KB to 22 KB.
 *
 * Scoped to mercury on purpose. The asset is deliberately grey, and no CSS filter
 * can give a desaturated pixel a brand hue, so the other eight themes keep their
 * existing accent bar rather than being handed a foreign grey lion.
 */
export function LionMark({ size = 40, className }: LionMarkProps) {
    return (
        <img
            src={lion}
            alt=""
            aria-hidden="true"
            width={size}
            height={size}
            style={{ height: size, width: size }}
            // A tight silver bloom. The mark is line art, so without it the hair
            // lines sit flat on the chrome and read as noise rather than metal.
            className={cn("drop-shadow-[0_0_7px_rgba(203,212,222,0.32)]", className)}
        />
    );
}
