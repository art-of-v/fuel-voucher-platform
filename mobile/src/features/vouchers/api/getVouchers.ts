import { apiFetch } from '../../../core/api/apiClient';
import type { Voucher, Order } from '../../../core/types/api';

export async function getMyVouchers(): Promise<Voucher[]> {
  const response = await apiFetch('/api/vouchers/my');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw new Error('Failed to fetch vouchers');
  }
  const data = await response.json();
  return (Array.isArray(data) ? data : []).map(mapVoucher);
}

function mapVoucher(v: any): Voucher {
  return {
    id: v.id,
    provider: v.provider,
    fuelType: v.fuelType ?? v.fuelTypeId ?? '',
    fuelName: v.fuelName,
    amount: v.amount ?? v.liters ?? 0,
    status: v.status?.toLowerCase() ?? 'active',
    // No backend field today; leave it unset so the UI renders the
    // locale-correct litre suffix (`10 л` in uk) instead of a hardcoded 'L'.
    unit: v.unit,
    qrCodeUrl: v.qrCodeUrl,
    qrCodeData: v.qrCodeData ?? v.qrPayload,
    externalId: v.externalId ?? v.voucherNumber,
    imageUrl: v.imageUrl ?? v.image_url ?? null,
    expirationDate: v.expirationDate,
    source: v.source ?? undefined,
    legalEntityId: v.legalEntityId ?? null,
    workerUserId: v.workerUserId ?? null,
    workerFirstName: v.workerFirstName ?? null,
    workerLastName: v.workerLastName ?? null,
    fuelSubtype: v.fuelSubtype ?? null,
    redemptionRules: v.redemptionRules ?? null,
  };
}

/**
 * Soft-deletes the caller's own unpaid (PendingPayment) checkout. 404 also covers
 * "already gone", so it is treated as success; any other failure throws and the
 * UI keeps the row.
 */
export async function deleteMyOrder(orderId: string): Promise<void> {
  const response = await apiFetch(`/api/purchases/${orderId}`, { method: 'DELETE' });
  if (!response.ok && response.status !== 404) {
    throw new Error('Failed to delete order');
  }
}

export async function getMyOrders(): Promise<Order[]> {
  const response = await apiFetch('/api/sync/orders');
  if (!response.ok) {
    if (response.status === 401) return [];
    throw new Error('Failed to fetch orders');
  }
  const data = await response.json();
  const statusMap: Record<string, Order['status']> = {
    PendingPayment: 'PENDING_PAYMENT',
    Paid: 'PENDING_FULFILLMENT',
    PendingFulfillment: 'PENDING_FULFILLMENT',
    PartiallyFulfilled: 'PENDING_FULFILLMENT',
    Fulfilled: 'FULFILLED',
    Refunded: 'REFUNDED',
    PartiallyRefunded: 'PARTIALLY_REFUNDED',
    Cancelled: 'REFUNDED',
  };
  return data.map((o: any) => {
    const mappedStatus = statusMap[o.status];
    if (!mappedStatus) {
      console.warn(`[getVouchers] Unknown order status "${o.status}", falling back to PENDING_FULFILLMENT`);
    }
    return {
      ...o,
      status: mappedStatus ?? 'PENDING_FULFILLMENT',
      createdAt: o.createdAtUtc ?? o.createdAt,
      fulfilledAt: o.fulfilledAtUtc ?? o.fulfilledAt ?? null,
      fuelType: o.fuelType ?? o.fuelTypeId ?? '',
      fuelName: o.fuelName,
      monobankPaymentUrl: o.monobankPaymentUrl ?? undefined,
      monobankInvoiceId: o.monobankInvoiceId ?? undefined,
      legalEntityId: o.legalEntityId ?? null,
      vouchers: Array.isArray(o.vouchers) ? o.vouchers.map(mapVoucher) : [],
      lineItems: Array.isArray(o.lineItems) ? o.lineItems.map((li: any) => ({
        id: li.id,
        provider: li.provider,
        fuelTypeId: li.fuelTypeId ?? li.fuelType ?? '',
        liters: li.liters,
        quantity: li.quantity,
      })) : [],
    };
  });
}
