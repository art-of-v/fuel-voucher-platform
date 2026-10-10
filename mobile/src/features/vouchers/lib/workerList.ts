import type { Order, Voucher } from '../../../core/types/api';

/**
 * The receipt narrowing the worker list does when filters are applied.
 *
 * Lives beside the screen because it is the one piece of behaviour that is not a plain call into
 * `listControls`: the list is receipt-grouped, so filtering has to decide what happens to a receipt
 * whose vouchers were all filtered out. Extracting it here makes that decision testable without a
 * renderer.
 */

/** Every voucher the worker holds, receipts and loose alike, in display order. */
export function collectWorkerListVouchers(
  issuanceOrders: Order[],
  looseVouchers: Voucher[],
): Voucher[] {
  return [...issuanceOrders.flatMap((o) => o.vouchers ?? []), ...looseVouchers];
}

/**
 * Narrow each receipt to the vouchers that survived the filters, and drop a receipt left with none.
 *
 * Dropping rather than showing an empty card is deliberate: a receipt with no visible vouchers would
 * read to a worker as "this handover gave me nothing", which is the opposite of what the filters
 * mean. It is also sorted by the soonest-expiring voucher it still has, so the receipt order follows
 * the sort the worker chose.
 */
export function narrowIssuanceOrders(
  issuanceOrders: Order[],
  visibleVoucherIds: Set<string>,
): Order[] {
  return issuanceOrders
    .map((order) => ({
      ...order,
      vouchers: (order.vouchers ?? []).filter((v) => visibleVoucherIds.has(v.id)),
    }))
    .filter((order) => order.vouchers.length > 0)
    .sort(
      (a, b) =>
        new Date(a.vouchers[0].expirationDate ?? 0).getTime() -
        new Date(b.vouchers[0].expirationDate ?? 0).getTime(),
    );
}
