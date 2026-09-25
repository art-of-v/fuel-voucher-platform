/**
 * In-app notification, as returned by `GET /api/notifications`.
 *
 * Named `AppNotification` rather than `Notification` on purpose: the latter is a
 * DOM/global type name, and shadowing it invites confusing type errors.
 *
 * The backend producer only emits an order-fulfilled event today, with a
 * hardcoded title and message and no `type`/`orderId` — so a row carries no
 * navigation target. Tapping it marks it read; it does not deep-link anywhere.
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
