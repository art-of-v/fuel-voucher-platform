import { useEffect, useState } from 'react';
import { AppState, type AppStateStatus } from 'react-native';

/**
 * `true` while the app is foregrounded (`AppState === 'active'`), `false` while it
 * is backgrounded or inactive.
 *
 * Used to pause timer-driven polling while the app is suspended. React Native has
 * no window-focus concept for TanStack Query, so a `refetchInterval` otherwise
 * keeps its timer across a background/foreground cycle and fires on resume —
 * racing the token-refresh single-flight guard, the shape behind the #26
 * spurious-logout (defense-in-depth; planning #45).
 *
 * Initialised `true` because a component reading this mounts while the app is
 * foregrounded; the listener corrects it on the first transition.
 */
export function useAppStateActive(): boolean {
  const [isActive, setIsActive] = useState(true);

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state: AppStateStatus) => {
      setIsActive(state === 'active');
    });
    return () => subscription.remove();
  }, []);

  return isActive;
}
