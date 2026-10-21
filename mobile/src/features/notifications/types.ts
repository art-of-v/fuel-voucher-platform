/**
 * In-app notification, as returned by `GET /api/notifications`.
 *
 * Named `AppNotification` rather than `Notification` on purpose: the latter is a
 * DOM/global type name, and shadowing it invites confusing type errors.
 *
 * `type` says what the row is about and the ids say what it opens. They are the
 * contract; `message` is prose, it is translated on the client, and it changes when
 * the wording changes. A client that has to understand the copy to route a
 * notification is a client that breaks when the copy is rewritten.
 *
 * The vocabulary is the one the push payload uses (`data.type` in the backend's
 * NotificationService), so a push and the in-app row it leaves behind route the
 * same way:
 *
 *  - `order_fulfilled` - `orderId` opens that order in the wallet;
 *  - `voucher_issued` - `legalEntityId` opens the worker context of the company
 *    that issued the fuel. A worker may work for several companies (W1), so the
 *    company has to come from the notification and not from whatever context the
 *    app happened to have open;
 *  - `generic` - a plain row: read it, done. No target.
 *
 * A row written before the type existed reads as `generic`, and a target may be
 * gone by the time the row is read - a company can be deleted, an order can be
 * deleted - so both ids are nullable and the client checks before it navigates.
 */

/** The kinds of notification the backend can write. Mirrors the backend's `NotificationTypes`. */
export type NotificationType = 'generic' | 'order_fulfilled' | 'voucher_issued';

export interface AppNotification {
  id: string;
  title: string;
  message: string;
  isRead: boolean;
  /** ISO-8601 UTC timestamp. */
  createdAt: string;
  /** What the row is about. */
  type: NotificationType;
  /** The company whose worker context the row opens, when it opens one. */
  legalEntityId: string | null;
  /** The order the row opens, when it opens one. */
  orderId: string | null;
}

/** Response of `PATCH /api/notifications/{id}/read`. */
export interface MarkNotificationReadResult {
  id: string;
  isRead: boolean;
}
