/**
 * How the per-fuel below-cost opt-in reads in the pricing editor.
 *
 * The editor keeps a draft of what the operator has touched and falls back to the stored value for
 * everything else. Getting this wrong is not cosmetic: a fuel that IS opted in rendered as unchecked
 * the moment the editor opened, so the operator saw "off" for a permission that was on. Saving did
 * not clear it - the submit path already fell back to the stored value - which made the display a
 * lie the operator could not act on and had no way to see.
 */
export function resolveAllowBelowCost(
  draft: boolean | undefined,
  stored: boolean | undefined,
): boolean {
  // `??` and not `||`: an explicit `false` in the draft must win over a stored `true`, because
  // un-ticking the box is the one action that has to be able to turn the permission off.
  return draft ?? stored ?? false;
}