import { useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef } from 'react';

/**
 * Runs a SILENT refresh every time the screen regains focus.
 *
 * Why this exists rather than TanStack Query's `refetchOnWindowFocus` (#164): that option listens
 * to `window.focus` / `visibilitychange`, neither of which React Native has, so it never fires —
 * the repo documents this in `useAppStateActive` and `docs/MOBILE-ARCHITECTURE.md`. Tabs here are
 * real routes on a stack, so revisiting one re-focuses an already-mounted screen instead of
 * remounting it, and a mount-keyed effect never runs again. Focus is the only signal that says
 * "the user is looking at this again, and something may have changed".
 *
 * Two properties matter and are why this is a hook rather than a one-line call in each screen:
 *
 * - **Silent.** The caller passes a refresh that does not flip its loading flag, so tapping a tab
 *   never flashes the full-screen loader over data the user is already reading.
 * - **At most one in flight.** Tapping between tabs must not stack requests, and a burst racing
 *   `apiFetch`'s token-refresh single-flight is the shape of the old #26 spurious logouts.
 *
 * The callback is held in a ref so a caller passing an inline arrow — which is every caller — does
 * not change the effect identity on every render and refetch in a loop.
 */
export function useRefreshOnFocus(refresh: () => unknown | Promise<unknown>, enabled = true): void {
  const latest = useRef(refresh);
  const inFlight = useRef(false);

  useEffect(() => {
    latest.current = refresh;
  }, [refresh]);

  useFocusEffect(
    useCallback(() => {
      if (!enabled || inFlight.current) return;

      inFlight.current = true;
      // The caller's refresh owns its own error surface (surfacing a message, a retry). Swallowing
      // the rejection here only stops this background fire-and-forget from becoming an unhandled
      // promise rejection; the guard is cleared either way so focus keeps working.
      void Promise.resolve(latest.current())
        .catch(() => undefined)
        .finally(() => {
          inFlight.current = false;
        });
    }, [enabled]),
  );
}
