import { apiFetch } from '../../../core/api/apiClient';
import type { AppNotification, MarkNotificationReadResult, NotificationType } from '../types';

/**
 * The caller's own notifications, newest-first. A 401 is treated as "logged
 * out" and yields an empty list rather than an error, matching `getMyVouchers`.
 */
export async function getNotifications(): Promise<AppNotification[]> {
  const response = await apiFetch('/api/notifications');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw new Error('Failed to fetch notifications');
  }
  const data = await response.json();
  return (Array.isArray(data) ? data : []).map(mapNotification);
}

/** Marks one notification read. 404 (missing / not the caller's) throws. */
export async function markNotificationRead(id: string): Promise<MarkNotificationReadResult> {
  const response = await apiFetch(`/api/notifications/${id}/read`, { method: 'PATCH' });
  if (!response.ok) {
    throw new Error('Failed to mark notification read');
  }
  const data = await response.json();
  return { id: data.id ?? id, isRead: data.isRead ?? true };
}

function mapNotification(n: any): AppNotification {
  return {
    id: n.id,
    title: n.title ?? '',
    message: n.message ?? '',
    isRead: n.isRead ?? false,
    createdAt: n.createdAt ?? n.createdAtUtc ?? '',
    type: toNotificationType(n.type),
    legalEntityId: n.legalEntityId ?? null,
    orderId: n.orderId ?? null,
  };
}

/**
 * A type this build does not know reads as `generic`: it is a row the customer still reads,
 * it is simply a row this build cannot route. A row written before the type existed has
 * none at all, and it means the same thing.
 */
function toNotificationType(value: unknown): NotificationType {
  return value === 'order_fulfilled' || value === 'voucher_issued' ? value : 'generic';
}
