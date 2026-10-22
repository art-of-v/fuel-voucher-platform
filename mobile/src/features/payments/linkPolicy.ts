/**
 * What the payment screen may load, and what it may hand to the operating system.
 *
 * WHY THIS EXISTS
 * `/pay` renders Monobank's page inside a WebView: a remote page we do not control, on the
 * one screen where the customer is about to type a card. Two channels let that page drive
 * the app, and both used to be wide open:
 *
 *  1. A navigation inside the frame. `react-native-webview` does NOT treat its origin
 *     whitelist as a restriction - a URL that is not on the list is handed to the OS
 *     (`Linking.openURL`) instead of being loaded here. The library documents it: "If the
 *     user taps to navigate to a new page but the new page is not in this whitelist, we will
 *     open the URL in Safari." So a narrow whitelist is the wrong way round: it would make
 *     the app itself the thing that opens `intent://…` and `tel:…` on the customer's phone.
 *  2. A `postMessage` from the frame. Monobank's widget asks its host to open a link
 *     (`monopay-link`), and the app used to open whatever value came with it - no scheme
 *     check, no host check, no question.
 *
 * THE RULE
 * The frame loads HTTPS and nothing else. The OS is handed HTTPS and nothing else.
 * Everything else is refused, quietly: a refused link is not an error the customer has to
 * read about, it is a link the page was never entitled to open.
 *
 * Monobank's own links are HTTPS universal links (`https://mbnk.app/or/…`), so the
 * hand-off to the Monobank app keeps working under this rule. The app's own deep links
 * (`fuelflow://`) are ours to route, not to hand to the OS - `Linking.openURL` on them
 * would relaunch the app on a URL the page chose.
 */

/** Matches the scheme of a URL: `https://…` -> `https`, `intent://…` -> `intent`. */
const SCHEME = /^([A-Za-z][A-Za-z0-9+\-.]*):/;

/** The scheme of a URL, lower-cased, or `null` when it has none. */
export function schemeOf(url: string | undefined | null): string | null {
  if (!url) return null;
  const match = SCHEME.exec(url.trim());
  return match ? match[1].toLowerCase() : null;
}

/**
 * True when the app may hand this URL to the operating system.
 *
 * HTTPS only. On Android an `intent://` URI is dispatched to an arbitrary exported
 * activity, on iOS any registered custom scheme opens, and `sms:`/`tel:` are accepted
 * everywhere - so the scheme is the allow-list, and `https` is the only one on it.
 */
export function isOpenableUrl(url: string | undefined | null): boolean {
  return schemeOf(url) === 'https';
}

/**
 * True when the frame may load this URL.
 *
 * HTTPS, plus the blank page the WebView itself uses. Plain HTTP is refused: the frame is
 * the customer's card form, and a payment page that arrives unencrypted over a network
 * anyone can read is not a payment page.
 */
export function isFrameLoadableUrl(url: string | undefined | null): boolean {
  if (!url) return false;
  const trimmed = url.trim();
  if (trimmed.toLowerCase() === 'about:blank') return true;
  return schemeOf(trimmed) === 'https';
}
