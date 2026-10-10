import type { Order, Voucher } from '../../../core/types/api';
import { collectWorkerListVouchers, narrowIssuanceOrders } from './workerList';

function v(id: string, expirationDate?: string): Voucher {
  return {
    id,
    provider: 'OKKO',
    fuelType: 'a95',
    amount: 10,
    status: 'active',
    expirationDate,
  } as Voucher;
}

function receipt(id: string, vouchers: Voucher[]): Order {
  return { id, vouchers, status: 'FULFILLED', provider: 'OKKO' } as unknown as Order;
}

describe('collectWorkerListVouchers', () => {
  it('gathers receipts and loose vouchers into one list', () => {
    const list = collectWorkerListVouchers([receipt('r1', [v('a'), v('b')])], [v('c')]);
    expect(list.map((x) => x.id)).toEqual(['a', 'b', 'c']);
  });

  it('tolerates a receipt with no vouchers nested', () => {
    const list = collectWorkerListVouchers([receipt('r1', []), receipt('r2', [v('a')])], []);
    expect(list.map((x) => x.id)).toEqual(['a']);
  });
});

describe('narrowIssuanceOrders', () => {
  const r1 = receipt('r1', [v('a', '2026-12-01'), v('b', '2026-11-01')]);
  const r2 = receipt('r2', [v('c', '2026-10-15')]);

  it('keeps only the visible vouchers inside each receipt', () => {
    const [narrowed] = narrowIssuanceOrders([r1], new Set(['b']));
    expect(narrowed.vouchers.map((x) => x.id)).toEqual(['b']);
  });

  it('drops a receipt left with nothing - it is not an empty handover', () => {
    // Showing an empty card would read as "this handover gave me nothing", the opposite of a filter.
    const narrowed = narrowIssuanceOrders([r1, r2], new Set(['a']));
    expect(narrowed.map((o) => o.id)).toEqual(['r1']);
  });

  it('does not mutate the orders it was given', () => {
    narrowIssuanceOrders([r1], new Set(['a']));
    expect(r1.vouchers).toHaveLength(2);
  });

  it('orders receipts by the soonest expiry among the vouchers they still show', () => {
    // r2 expires sooner, so it leads even though it is declared second.
    const narrowed = narrowIssuanceOrders([r1, r2], new Set(['a', 'c']));
    expect(narrowed.map((o) => o.id)).toEqual(['r2', 'r1']);
  });

  it('returns nothing when no voucher is visible', () => {
    expect(narrowIssuanceOrders([r1, r2], new Set())).toEqual([]);
  });
});
