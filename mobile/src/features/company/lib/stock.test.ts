import {
  filterVouchersByContext,
  filterOrdersByContext,
  groupCompanyStock,
} from './stock';
import type { Order, Voucher } from '../../../core/types/api';

function mkVoucher(overrides: Partial<Voucher> = {}): Voucher {
  return {
    id: 'v1',
    provider: 'OKKO',
    fuelType: 'a95',
    amount: 10,
    status: 'active',
    legalEntityId: null,
    workerUserId: null,
    ...overrides,
  };
}

function mkOrder(overrides: Partial<Order> = {}): Order {
  return {
    id: 'o1',
    provider: 'OKKO',
    fuelType: 'a95',
    liters: 10,
    quantity: 1,
    price: 100,
    status: 'FULFILLED',
    createdAt: '2026-01-01T00:00:00Z',
    fulfilledAt: null,
    legalEntityId: null,
    lineItems: [],
    ...overrides,
  };
}

describe('filterVouchersByContext', () => {
  const personal = mkVoucher({ id: 'p', legalEntityId: null });
  const acme1 = mkVoucher({ id: 'a1', legalEntityId: 'acme' });
  const acme2 = mkVoucher({ id: 'a2', legalEntityId: 'acme' });
  const globex = mkVoucher({ id: 'g1', legalEntityId: 'globex' });
  const all = [personal, acme1, acme2, globex];

  it('returns only personal vouchers for the personal (null) context', () => {
    expect(filterVouchersByContext(all, null)).toEqual([personal]);
  });

  it('returns only the active company vouchers for a company context', () => {
    expect(filterVouchersByContext(all, 'acme')).toEqual([acme1, acme2]);
  });

  it('never leaks another company into the active one', () => {
    expect(filterVouchersByContext(all, 'globex')).toEqual([globex]);
  });

  it('returns an empty list for a company the user has no vouchers in', () => {
    expect(filterVouchersByContext(all, 'initech')).toEqual([]);
  });
});

describe('filterOrdersByContext', () => {
  const personal = mkOrder({ id: 'p', legalEntityId: null });
  const acme = mkOrder({ id: 'a', legalEntityId: 'acme' });
  const globex = mkOrder({ id: 'g', legalEntityId: 'globex' });
  const all = [personal, acme, globex];

  it('returns only personal orders for the personal (null) context', () => {
    expect(filterOrdersByContext(all, null)).toEqual([personal]);
  });

  it('returns only the active company orders for a company context', () => {
    expect(filterOrdersByContext(all, 'acme')).toEqual([acme]);
  });
});

describe('groupCompanyStock', () => {
  it('puts vouchers with no worker into the pool', () => {
    const vouchers = [
      mkVoucher({ id: 'a', legalEntityId: 'acme', workerUserId: null, amount: 50 }),
      mkVoucher({ id: 'b', legalEntityId: 'acme', workerUserId: null, amount: 100 }),
    ];
    const stock = groupCompanyStock(vouchers);
    expect(stock.pool.map((v) => v.id)).toEqual(['a', 'b']);
    expect(stock.poolLiters).toBe(150);
    expect(stock.workers).toEqual([]);
  });

  it('groups distributed vouchers per worker and sums their liters', () => {
    const vouchers = [
      mkVoucher({ id: 'w1a', legalEntityId: 'acme', workerUserId: 'u1', workerFirstName: 'Іван', workerLastName: 'П', amount: 20 }),
      mkVoucher({ id: 'w1b', legalEntityId: 'acme', workerUserId: 'u1', workerFirstName: 'Іван', workerLastName: 'П', amount: 30 }),
      mkVoucher({ id: 'w2', legalEntityId: 'acme', workerUserId: 'u2', workerFirstName: 'Анна', workerLastName: 'К', amount: 15 }),
    ];
    const stock = groupCompanyStock(vouchers);
    expect(stock.pool).toEqual([]);
    expect(stock.workers).toHaveLength(2);
    // Ordered by name: "Анна К" before "Іван П".
    expect(stock.workers[0]).toMatchObject({ workerUserId: 'u2', workerName: 'Анна К', liters: 15 });
    expect(stock.workers[1]).toMatchObject({ workerUserId: 'u1', workerName: 'Іван П', liters: 50 });
    expect(stock.workers[1].vouchers.map((v) => v.id)).toEqual(['w1a', 'w1b']);
  });

  it('splits a mixed company into pool + workers', () => {
    const vouchers = [
      mkVoucher({ id: 'pool', legalEntityId: 'acme', workerUserId: null, amount: 40 }),
      mkVoucher({ id: 'held', legalEntityId: 'acme', workerUserId: 'u1', workerFirstName: 'Іван', amount: 20 }),
    ];
    const stock = groupCompanyStock(vouchers);
    expect(stock.pool.map((v) => v.id)).toEqual(['pool']);
    expect(stock.workers.map((w) => w.workerUserId)).toEqual(['u1']);
  });

  it('uses a null worker name when the worker has no name on the voucher', () => {
    const vouchers = [
      mkVoucher({ id: 'x', legalEntityId: 'acme', workerUserId: 'u9', workerFirstName: null, workerLastName: null, amount: 10 }),
    ];
    const stock = groupCompanyStock(vouchers);
    expect(stock.workers[0].workerName).toBeNull();
  });
});
