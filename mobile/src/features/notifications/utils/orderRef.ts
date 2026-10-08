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

// A 6-char tail is unique enough for a human-facing reference and matches the
// display the owner asked for in planning #132 (`#…f9da72`).
const TAIL_LENGTH = 6;

export interface OrderNotificationRef {
  /** The full order id the message embeds, or null if it carries none. */
  orderId: string | null;
  /** The message with any embedded order id shortened to `…<tail>`. */
  display: string;
}

export function parseOrderNotification(message: string): OrderNotificationRef {
  const match = message.match(ORDER_ID_RE);
  if (!match) {
    return { orderId: null, display: message };
  }
  const orderId = match[0];
  const display = message.replace(orderId, `…${orderId.slice(-TAIL_LENGTH)}`);
  return { orderId, display };
}
