/**
 * Pure snap maths for the map's gesture-driven ranking sheet. Kept apart from the
 * component (and free of any reanimated/RN import) so the release behaviour — which snap a
 * flick lands on, when a drag dismisses — is unit-testable on its own.
 *
 * Offsets are `translateY` values measured from the fully-open position: `0` is open to the
 * top (full), larger values push the sheet down, and the largest offset is the resting
 * "peek". Callers pass them ascending.
 *
 * Both functions carry the `'worklet'` directive so reanimated can run them on the UI thread
 * from the pan handler; the directive is an inert string literal under plain JS/Jest.
 */

/**
 * Projects the drag by its release velocity, then returns the nearest snap offset — so a
 * fast flick carries past the midpoint to the next snap instead of falling back to whichever
 * is closest at the instant the finger lifts.
 */
export function nearestSnapOffset(
  current: number,
  velocityY: number,
  offsets: number[],
  projection = 0.15,
): number {
  'worklet';
  const projected = current + velocityY * projection;
  return offsets.reduce(
    (best, o) => (Math.abs(o - projected) < Math.abs(best - projected) ? o : best),
    offsets[0],
  );
}

export interface SheetRelease {
  /** The snap offset to spring to (even when dismissing, so the exit starts from a snap). */
  offset: number;
  /** True when the gesture should close the sheet rather than settle on a snap. */
  dismiss: boolean;
}

/**
 * Resolves where a pan release leaves the sheet. It dismisses when the release is dragged
 * well past the peek offset, or flicked down hard while already resting at peek; otherwise it
 * settles on the velocity-projected nearest snap. `offsets` must be ascending (last = peek).
 */
export function resolveSheetRelease(
  current: number,
  velocityY: number,
  offsets: number[],
  opts: { dismissMargin?: number; flingVelocity?: number } = {},
): SheetRelease {
  'worklet';
  const dismissMargin = opts.dismissMargin ?? 60;
  const flingVelocity = opts.flingVelocity ?? 900;
  const peek = offsets[offsets.length - 1];
  const projected = current + velocityY * 0.15;
  if (projected > peek + dismissMargin) return { offset: peek, dismiss: true };
  if (Math.abs(current - peek) < 24 && velocityY > flingVelocity) {
    return { offset: peek, dismiss: true };
  }
  return { offset: nearestSnapOffset(current, velocityY, offsets), dismiss: false };
}
