/**
 * The order-fulfilled notification copy embeds the full order GUID
 * (`Ваше замовлення #<guid> виконано…`, built server-side in NotificationService).
 * A full GUID is unreadable in the list and gets clipped mid-string, so two
 * different orders look identical (planning #132).
 *
 * It is also the *only* place the in-app record carries the order id — the
 * notification DTO has no structured `orderId` field — so the id a tapped row
 * deep-links to has to be recovered from the message here rather than added via
 * a schema change.
 *
 * `parseOrderNotification` does both in one pass: it pulls the full id out (for
 * the `/my-codes?orderId=…` deep-link, matching the push tap wired up in
 * `notificationResponse.ts`) and returns the message with that id shortened to a
 * readable tail for display. A message with no embedded id comes back unchanged
 * with a null id, so a future notification type degrades to a plain,
 * non-navigating row.
 */

// 8-4-4-4-12 hex, case-insensitive. Unanchored: the id sits mid-sentence.
const ORDER_ID_RE = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;

/**
 * The `#<id>` fragment as it appears in the server-built sentence, so a collapsed
 * row can drop the whole thing rather than only the id - leaving a bare `#` behind
 * mid-sentence is worse than the guid was.
 */
export const ORDER_ID_FRAGMENT =
  /#\s*[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;

// A 6-char tail is unique enough for a human-facing reference and matches the
// display the owner asked for in planning #132 (`#.f9da72`).
const TAIL_LENGTH = 6;

export interface OrderNotificationRef {
  /** The full order id the message embeds, or null if it carries none. */
  orderId: string | null;
  /** The message with any embedded order id shortened to `…<tail>`. */
  display: string;
  /**
   * The message with the whole `#<id>` fragment removed, for a collapsed row.
   * Dropping only the guid would leave a bare `#` mid-sentence, which reads worse
   * than the guid did.
   */
  preview: string;
}

export function parseOrderNotification(message: string): OrderNotificationRef {
  // A global regexp carries `lastIndex` between calls.
  ORDER_ID_FRAGMENT.lastIndex = 0;
  const match = message.match(ORDER_ID_RE);
  if (!match) {
    return { orderId: null, display: message, preview: message };
  }
  const orderId = match[0];
  const display = message.replace(orderId, `…${orderId.slice(-TAIL_LENGTH)}`);
  ORDER_ID_FRAGMENT.lastIndex = 0;
  const preview = message
    .replace(ORDER_ID_FRAGMENT, '')
    .replace(/\s{2,}/g, ' ')
    .trim();
  return { orderId, display, preview };
}
