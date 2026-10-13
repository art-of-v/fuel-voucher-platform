import { useCallback, useEffect, useState } from 'react';

import { getFeedbackDeepLink } from '../api/feedback';

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
 * off either. The tab appears once the server has positively confirmed it.
 *
 * Fetches the deep link rather than a bare flag so the URL the screen opens is the one the server
 * already signed — the link and the gate cannot drift apart.
 */
export function useFeedbackChannel(): { available: boolean; deepLink: string | null } {
  const [deepLink, setDeepLink] = useState<string | null>(null);
  const [resolved, setResolved] = useState(false);

  const load = useCallback(async () => {
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

  return { available: resolved && deepLink !== null, deepLink };
}