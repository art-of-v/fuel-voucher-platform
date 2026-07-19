import { apiFetch } from '../../../core/api/apiClient';
import type { Voucher, Order } from '../../../core/types/api';

export async function getMyVouchers(): Promise<Voucher[]> {
  const response = await apiFetch('/api/vouchers/my');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw new Error('Failed to fetch vouchers');
  }
  return response.json();
}

export async function getMyOrders(): Promise<Order[]> {
  const response = await apiFetch('/api/sync/orders');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw new Error('Failed to fetch orders');
  }
  const data = await response.json();
  const statusMap: Record<string, Order['status']> = {
    PendingPayment: 'PENDING_FULFILLMENT',
    Paid: 'PENDING_FULFILLMENT',
    PendingFulfillment: 'PENDING_FULFILLMENT',
    PartiallyFulfilled: 'PENDING_FULFILLMENT',
    Fulfilled: 'FULFILLED',
    Refunded: 'REFUNDED',
    Cancelled: 'REFUNDED',
  };
  return data.map((o: any) => ({
    ...o,
    status: statusMap[o.status] ?? 'PENDING_FULFILLMENT',
    createdAt: o.createdAtUtc ?? o.createdAt,
    fulfilledAt: o.fulfilledAtUtc ?? o.fulfilledAt ?? null,
    fuelType: o.fuelType ?? o.fuelTypeId ?? '',
  }));
}
