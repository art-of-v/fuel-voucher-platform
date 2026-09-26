import { useEffect } from 'react';
import { useRouter } from 'expo-router';
import * as Notifications from 'expo-notifications';

/**
 * The RECEIVE/TAP half of push. Registration (minting a token + upserting it to
 * the backend) lives in ./push; this hook wires up what happens when a push is
 * tapped, and how a push is presented while the app is foregrounded.
 *
 * Destination: an "order fulfilled" push carries `type: "order_fulfilled"` and an
 * `orderId` in `data` (see the backend NotificationService), so a tap deep-links to
 * that order in the wallet — `/my-codes?orderId=…`, which expands that order's card.
 * Any other push, or a malformed payload, falls back to the in-app notifications
 * list `/notifications` (the list from #28a / PR #653). The branch lives in
 * `openTarget`, which reads the tapped response's `notification.request.content.data`.
 *
 * Two tap paths, both handled:
 *  - warm — the app is already running (foreground/background): the response listener.
 *  - cold — the app was killed and the tap launched it: getLastNotificationResponseAsync
 *           replays that one launching tap, so we don't strand the user on the default tab.
 *
 * Best-effort: failures are swallowed so a malformed payload can never crash
 * startup. No auth gate here — expo-router's guard bounces to /landing if a tap
 * somehow arrives while logged out.
 *
 * VERIFIABILITY: expo-notifications is a native module, so a real tap can only be
 * exercised in an EAS build on a device — never a simulator or Jest. This wiring
 * is unit-tested against a mocked SDK; on-device confirmation needs the native
 * rebuild that also carries slices 1+2 (it is not OTA-able).
 */
export function useNotificationTapRouting(): void {
  const router = useRouter();

  useEffect(() => {
    // Foreground presentation: iOS suppresses the system banner while the app is
    // open unless a handler opts in, so an in-app push would otherwise be silent
    // (and invisible during the on-device "is it working?" test). Badge stays off
    // — the in-app bell owns the unread count (see useNotifications).
    Notifications.setNotificationHandler({
      handleNotification: async () => ({
        shouldShowBanner: true,
        shouldShowList: true,
        shouldPlaySound: true,
        shouldSetBadge: false,
      }),
    });

    // Doubles as an after-unmount guard: cleanup flips it so a late-resolving
    // cold-start promise can't navigate on a torn-down tree.
    let handled = false;
    const openTarget = (response: Notifications.NotificationResponse | null) => {
      // Deep-link an "order fulfilled" push to that specific order in the wallet
      // (my-codes expands it); anything else — or a malformed payload — falls back
      // to the in-app notifications list. See NotificationService.TrySendPushAsync
      // for the payload contract.
      const data = response?.notification?.request?.content?.data as
        | { type?: unknown; orderId?: unknown }
        | undefined;
      if (data?.type === 'order_fulfilled' && typeof data.orderId === 'string' && data.orderId) {
        router.push({ pathname: '/my-codes', params: { orderId: data.orderId } });
        return;
      }
      router.push('/notifications');
    };

    // Cold start: replay the tap that launched the app (fires at most once).
    Notifications.getLastNotificationResponseAsync()
      .then((response) => {
        if (response && !handled) {
          handled = true;
          openTarget(response);
        }
      })
      .catch(() => {
        // best-effort: never block startup on a bad launch payload
      });

    // Warm: a tap while the app is running.
    const subscription = Notifications.addNotificationResponseReceivedListener((response) => {
      handled = true;
      openTarget(response);
    });

    return () => {
      handled = true;
      subscription.remove();
    };
  }, [router]);
}
