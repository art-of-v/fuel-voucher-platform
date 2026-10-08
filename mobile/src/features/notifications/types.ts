/**
 * In-app notification, as returned by `GET /api/notifications`.
 *
 * Named `AppNotification` rather than `Notification` on purpose: the latter is a
 * DOM/global type name, and shadowing it invites confusing type errors.
 *
 * The backend producer only emits an order-fulfilled event today, with a
 * hardcoded title and message and no structured `type`/`orderId`. The order id
 * is embedded in `message` instead, so an order row is still tappable — see
 * `utils/orderRef.ts`, which recovers it for the `/my-codes` deep-link.
 */
export interface AppNotification {
  id: string;
  title: string;
  message: string;
  isRead: boolean;
  /** ISO-8601 UTC timestamp. */
  createdAt: string;
}

/** Response of `PATCH /api/notifications/{id}/read`. */
export interface MarkNotificationReadResult {
  id: string;
  isRead: boolean;
}
