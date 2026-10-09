/**
 * Shared shape for "an invoice exists, now pay it" - the one thing every checkout hands
 * to the payment screen, whether it was just created or re-opened later from an order.
 */
export interface PaymentSession {
  /** Order id, for the request-time log line and future server-side correlation. */
  orderId: string;
  /**
   * Monobank's embeddable payment page (`pay.monobank.ua/frame/…`, because checkout asks
   * for `displayType: "iframe"`). This is what the in-app WebView renders.
   */
  pageUrl: string;
  /**
   * The Monobank-app deep link (`withAppUrl: true`), e.g. `https://mbnk.app/or/…`.
   *
   * Only returned at invoice-creation time and NOT persisted on the order - it is an opaque
   * redirector that cannot be reconstructed. So when an order is re-opened from the wallet
   * this is undefined, and the screen offers only the in-app page. Nullish is normal, not an
   * error: every consumer must treat it as optional.
   */
  appUrl?: string | null;
}

/** Route params for `/pay`. URLs travel as strings through the query. */
export interface PaymentRouteParams {
  url: string;
  appUrl?: string;
  orderId?: string;
}

/**
 * Builds the `/pay` href. Everything is encoded: the URLs carry query strings of their own
 * (`?orderId=…`), so an unencoded `&` would split the param and silently truncate the URL
 * the WebView then loads - a payment page that renders half a query is a dead checkout.
 */
export function paymentHref(session: PaymentSession): string {
  const params = new URLSearchParams({
    url: session.pageUrl,
    ...(session.orderId ? { orderId: session.orderId } : {}),
    ...(session.appUrl ? { appUrl: session.appUrl } : {}),
  });
  return `/pay?${params.toString()}`;
}

/**
 * True when a URL is one of OUR deep links - i.e. the post-payment redirect
 * (`fuelflow://payment-result`, or `fuelflow-staging://` on the TestFlight build).
 *
 * Compared against `expo-constants`' own scheme rather than a hard-coded list, so adding a
 * scheme in app.json / app.config.ts does not require remembering to update the check here.
 * A missed match means the redirect navigates the WebView away instead of closing it, which
 * is the bug this function exists to prevent.
 */
export function isAppDeepLink(url: string, appSchemes: string[]): boolean {
  return appSchemes.some((scheme) => url.toLowerCase().startsWith(`${scheme.toLowerCase()}://`));
}
