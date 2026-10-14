import { useCallback, useEffect, useRef, useState } from 'react';
import { AppState } from 'react-native';

import { getFeedbackDeepLink } from '../api/feedback';

/**
 * How long a foreground transition must be since the last check before we ask again.
 *
 * Not a cache: the gate is deliberately allowed to go stale, because the alternative is a tab that
 * stays hidden until the user kills and relaunches the app — which is exactly what an operator
 * enabling the channel during a test run looks like from their side. A minute is long enough that
 * a user alt-tabbing around does not re-request per switch, and short enough that the tab appears
 * while they are still looking for it.
 */
const FOREGROUND_RECHECK_MS = 60_000;

/**
 * Whether the feedback channel is offered to this customer.
 *
 * Server-decided, never client-decided: `GET /api/feedback/deep-link` answers 404 unless both the
 * `FeedbackBot:Enabled` app setting and the Telegram integration are provisioned. That is the same
 * shape as the renewal gate (`renewalConfig?.enabled` in `app/my-codes.tsx`), and it is why an
 * admin can take the channel out of service without a mobile release.
 *
 * Unknown until the answer arrives, and it stays unknown if the request fails: a network blip
 * must not silently remove the only way to report a problem, and must not flash the tab on and
 * off either. The tab appears once the server has positively confirmed it — and, because the check
 * is re-run on every foreground transition, a channel switched on after launch shows up without a
 * relaunch.
 *
 * Fetches the deep link rather than a bare flag so the URL the screen opens is the one the server
 * already signed — the link and the gate cannot drift apart.
 */
export function useFeedbackChannel(): { available: boolean; deepLink: string | null } {
  const [deepLink, setDeepLink] = useState<string | null>(null);
  const [resolved, setResolved] = useState(false);

  // Read by the AppState listener, which is registered once and must not re-subscribe on every
  // answer. Last attempt, not last success: a channel switched OFF server-side should also be
  // picked up on the next foreground rather than pinned on for the rest of the session.
  const lastCheckedAtRef = useRef(0);

  const load = useCallback(async () => {
    lastCheckedAtRef.current = Date.now();

    try {
      const link = await getFeedbackDeepLink();
      setDeepLink(link?.url ?? null);
    } catch {
      setDeepLink(null);
    } finally {
      setResolved(true);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (next) => {
      // 'active' is the only state worth acting on: a request fired while the app is backgrounded
      // can be suspended mid-flight and its answer applied to a screen nobody is looking at.
      if (next !== 'active') return;
      if (Date.now() - lastCheckedAtRef.current < FOREGROUND_RECHECK_MS) return;

      void load();
    });

    return () => subscription.remove();
  }, [load]);

  return { available: resolved && deepLink !== null, deepLink };
}
