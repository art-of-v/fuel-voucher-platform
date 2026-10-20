import { apiFetch } from '../../../core/api/apiClient';
import type { Voucher, Order } from '../../../core/types/api';

/**
 * The caller's wallet.
 *
 * The server pages this list, and it has to: the list carries the REDEEMABLE QR of every
 * voucher in it - the bearer material and a rendered PNG per row - so an unbounded list
 * meant that one customer with a few hundred vouchers made the API render all of their QR
 * images in a single request.
 *
 * The pages are fetched in sequence and concatenated, so the app still sees the whole
 * wallet. The paging is the server's bound on one request, not a change of what the
 * screen shows.
 */
export async function getMyVouchers(): Promise<Voucher[]> {
  const all: Voucher[] = [];
  const pageSize = 50;

  for (let page = 1; ; page++) {
    const response = await apiFetch(`/api/vouchers/my?page=${page}&pageSize=${pageSize}`);
    if (!response.ok) {
      // 401 is the signed-out case, and the original behaviour was to yield an empty
      // list rather than an error - so the pages fetched so far are kept.
      if (response.status === 401) return all;
      throw new Error('Failed to fetch vouchers');
    }

    const data = await response.json();
    const vouchers = (Array.isArray(data) ? data : []).map(mapVoucher);
    all.push(...vouchers);

    // The server's count decides, and the short page is only the fallback. The order
    // matters: the server is the one that knows how many rows it left out, and a page
    // shorter than the one we asked for does not mean the list ended - it means the
    // server serves smaller pages than we asked for.
    const total = Number(response.headers.get('X-Total-Count') ?? '0');
    if (total > 0) {
      if (all.length >= total) break;
    } else if (vouchers.length < pageSize) {
      break;
    }
  }

  return all;
}

function mapVoucher(v: any): Voucher {
  return {
    id: v.id,
    brand: v.brand,
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
    originOrderId: v.originOrderId ?? null,
    history: Array.isArray(v.history)
      ? v.history.map((h: any) => ({
          type: h.type,
          date: h.date,
          liters: h.liters ?? 0,
          amount: h.amount ?? null,
          validFrom: h.validFrom ?? null,
          validTo: h.validTo ?? null,
          termCode: h.termCode ?? null,
          isReplacement: h.isReplacement === true,
        }))
      : [],
  };
}

/**
 * Soft-deletes the caller's own unpaid (PendingPayment) checkout.
 * Throws on any non-2xx response so the UI can show an error and refetch.
 */
export async function deleteMyOrder(orderId: string): Promise<void> {
  const response = await apiFetch(`/api/purchases/${orderId}`, { method: 'DELETE' });
  if (!response.ok) {
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
      console.warn(
        `[getVouchers] Unknown order status "${o.status}", falling back to PENDING_FULFILLMENT`,
      );
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
      isRenewal: !!o.isRenewal,
      kind: o.kind ?? 'Purchase',
      vouchers: Array.isArray(o.vouchers) ? o.vouchers.map(mapVoucher) : [],
      lineItems: Array.isArray(o.lineItems)
        ? o.lineItems.map((li: any) => ({
            id: li.id,
            brand: li.brand,
            fuelTypeId: li.fuelTypeId ?? li.fuelType ?? '',
            liters: li.liters,
            quantity: li.quantity,
          }))
        : [],
    };
  });
}
