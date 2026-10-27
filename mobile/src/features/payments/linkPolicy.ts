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
 * The frame loads HTTPS and nothing else. The OS is handed HTTPS, plus the two links that
 * are Monobank's own way into its app (`https://mbnk.app/…` and `app://com.ftband.mono/…`,
 * both checked literally - see `isMonobankAppLink`). Everything else is refused, quietly: a
 * refused link is not an error the customer has to read about, it is a link the page was
 * never entitled to open.
 *
 * The app's own deep links (`fuelflow://`) are ours to route, not to hand to the OS -
 * `Linking.openURL` on them would relaunch the app on a URL the page chose.
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
 * Monobank's universal link to its app, the one its API issues as `appUrl`
 * (`https://mbnk.app/or/<invoiceId>`).
 */
const MONOBANK_UNIVERSAL_LINK = /^https:\/\/mbnk\.app(\/|$)/i;

/**
 * The custom scheme Monobank's own checkout page navigates to in order to reach its app:
 * `location.href = "app://com.ftband.mono" + path + search`. The host is the app's bundle
 * id, so this is exactly the Monobank app and not any other handler of the generic
 * `app:` scheme.
 */
const MONOBANK_APP_SCHEME = /^app:\/\/com\.ftband\.mono([/?#]|$)/i;

/**
 * True when a URL is Monobank's way INTO its app rather than a page to render.
 *
 * WHY THIS HAS TO LEAVE THE WEBVIEW
 * iOS does not resolve a universal link inside a WKWebView - it renders the web page at
 * `mbnk.app` instead. That page says "Це посилання потрібно відкривати у застосунку monobank"
 * and offers the app stores, which reads as "update the app" to a customer who already has
 * it; its own "Відкрити застосунок" button then navigates to the custom scheme above, which
 * the frame refuses to load for not being HTTPS. The result is a dead end on the payment
 * screen: no way into Monobank, so the invoice never reaches the app and never gets paid.
 * Production, 2026-10-27: an 862 ₴ invoice expired one hour after creation without a single
 * payment attempt (webhook statuses were `created` and `expired`, nothing else).
 *
 * So both links are handed to the operating system instead of being loaded. The host and
 * the bundle id are checked literally: `mbnk.app.evil.example` and `app://evil/` do not
 * match, and plain HTTP to the same host does not either.
 */
export function isMonobankAppLink(url: string | undefined | null): boolean {
  if (!url) return false;
  const trimmed = url.trim();
  return MONOBANK_UNIVERSAL_LINK.test(trimmed) || MONOBANK_APP_SCHEME.test(trimmed);
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
