import {
  filterVouchersByContext,
  filterOrdersByContext,
  groupCompanyStock,
  groupVouchersByProvider,
  buildWorkerRoster,
  filterRoster,
  ownerActionsForVoucher,
} from './stock';
import { PERSONAL_CONTEXT, type ResolvedContext } from './context';
import type { Order, Voucher } from '../../../core/types/api';

const ME = 'user-me';

function ownerContext(id: string): ResolvedContext {
  return { kind: 'owner', company: { id, name: id, edrpou: '1' }, membership: null };
}

function workerContext(id: string): ResolvedContext {
  return {
    kind: 'worker',
    company: { id, name: id, edrpou: '1' },
    membership: {
      memberId: 'm1',
      legalEntityId: id,
      name: id,
      edrpou: '1',
      ownerUserId: 'owner',
      isOwner: false,
      joinedAtUtc: '2026-01-01T00:00:00Z',
    },
  };
}

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
    expect(filterVouchersByContext(all, PERSONAL_CONTEXT, ME)).toEqual([personal]);
  });

  it('returns only the active company vouchers for an owner context', () => {
    expect(filterVouchersByContext(all, ownerContext('acme'), ME)).toEqual([acme1, acme2]);
  });

  it('never leaks another company into the active one', () => {
    expect(filterVouchersByContext(all, ownerContext('globex'), ME)).toEqual([globex]);
  });

  it('returns an empty list for a company the user has no vouchers in', () => {
    expect(filterVouchersByContext(all, ownerContext('initech'), ME)).toEqual([]);
  });
});

describe('filterVouchersByContext — worker context (epic #103 S5)', () => {
  const mine = mkVoucher({ id: 'mine', legalEntityId: 'acme', workerUserId: ME });
  const used = mkVoucher({ id: 'used', legalEntityId: 'acme', workerUserId: ME, status: 'used' });
  const pool = mkVoucher({ id: 'pool', legalEntityId: 'acme', workerUserId: null });
  const otherWorker = mkVoucher({
    id: 'theirs',
    legalEntityId: 'acme',
    workerUserId: 'user-other',
  });
  const otherCompany = mkVoucher({ id: 'globex', legalEntityId: 'globex', workerUserId: ME });
  const minePersonal = mkVoucher({ id: 'personal', legalEntityId: null, workerUserId: null });
  const all = [mine, used, pool, otherWorker, otherCompany, minePersonal];
  const context = workerContext('acme');

  it('shows only the fuel issued to this worker in that company', () => {
    expect(filterVouchersByContext(all, context, ME)).toEqual([mine, used]);
  });

  it("never shows the company pool or another worker's fuel", () => {
    const ids = filterVouchersByContext(all, context, ME).map((v) => v.id);
    expect(ids).not.toContain('pool');
    expect(ids).not.toContain('theirs');
  });

  it('never shows fuel from a company the worker works for elsewhere', () => {
    const ids = filterVouchersByContext(all, context, ME).map((v) => v.id);
    expect(ids).not.toContain('globex');
  });

  it("never shows the worker's personal vouchers inside a worker context", () => {
    const ids = filterVouchersByContext(all, context, ME).map((v) => v.id);
    expect(ids).not.toContain('personal');
  });

  it('fails closed when the current user id is unknown', () => {
    expect(filterVouchersByContext(all, context, undefined)).toEqual([]);
    expect(filterVouchersByContext(all, context, null)).toEqual([]);
  });
});

describe('filterOrdersByContext', () => {
  const personal = mkOrder({ id: 'p', legalEntityId: null });
  const acme = mkOrder({ id: 'a', legalEntityId: 'acme' });
  const globex = mkOrder({ id: 'g', legalEntityId: 'globex' });
  const all = [personal, acme, globex];

  it('returns only personal orders for the personal context', () => {
    expect(filterOrdersByContext(all, PERSONAL_CONTEXT)).toEqual([personal]);
  });

  it('returns only the active company orders for an owner context', () => {
    expect(filterOrdersByContext(all, ownerContext('acme'))).toEqual([acme]);
  });

  it("returns no orders in a worker context — the employer's purchases are not the worker's", () => {
    expect(filterOrdersByContext(all, workerContext('acme'))).toEqual([]);
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
      mkVoucher({
        id: 'w1a',
        legalEntityId: 'acme',
        workerUserId: 'u1',
        workerFirstName: 'Іван',
        workerLastName: 'П',
        amount: 20,
      }),
      mkVoucher({
        id: 'w1b',
        legalEntityId: 'acme',
        workerUserId: 'u1',
        workerFirstName: 'Іван',
        workerLastName: 'П',
        amount: 30,
      }),
      mkVoucher({
        id: 'w2',
        legalEntityId: 'acme',
        workerUserId: 'u2',
        workerFirstName: 'Анна',
        workerLastName: 'К',
        amount: 15,
      }),
    ];
    const stock = groupCompanyStock(vouchers);
    expect(stock.pool).toEqual([]);
    expect(stock.workers).toHaveLength(2);
    // Ordered by name: "Анна К" before "Іван П".
    expect(stock.workers[0]).toMatchObject({
      workerUserId: 'u2',
      workerName: 'Анна К',
      liters: 15,
    });
    expect(stock.workers[1]).toMatchObject({
      workerUserId: 'u1',
      workerName: 'Іван П',
      liters: 50,
    });
    expect(stock.workers[1].vouchers.map((v) => v.id)).toEqual(['w1a', 'w1b']);
  });

  it('splits a mixed company into pool + workers', () => {
    const vouchers = [
      mkVoucher({ id: 'pool', legalEntityId: 'acme', workerUserId: null, amount: 40 }),
      mkVoucher({
        id: 'held',
        legalEntityId: 'acme',
        workerUserId: 'u1',
        workerFirstName: 'Іван',
        amount: 20,
      }),
    ];
    const stock = groupCompanyStock(vouchers);
    expect(stock.pool.map((v) => v.id)).toEqual(['pool']);
    expect(stock.workers.map((w) => w.workerUserId)).toEqual(['u1']);
  });

  it('uses a null worker name when the worker has no name on the voucher', () => {
    const vouchers = [
      mkVoucher({
        id: 'x',
        legalEntityId: 'acme',
        workerUserId: 'u9',
        workerFirstName: null,
        workerLastName: null,
        amount: 10,
      }),
    ];
    const stock = groupCompanyStock(vouchers);
    expect(stock.workers[0].workerName).toBeNull();
  });
});
describe('ownerActionsForVoucher (#159)', () => {
  it('offers freeze and recall on an assigned voucher', () => {
    expect(ownerActionsForVoucher('assigned')).toEqual({
      canFreezeOrRecall: true,
      canUnblock: false,
    });
  });

  it('offers nothing to press on a redeemed voucher — the server would refuse', () => {
    // Planning #159: the owner screen used to render freeze/recall here and the tap
    // came back as «conflicts with the current state».
    const actions = ownerActionsForVoucher('used');
    expect(actions.canFreezeOrRecall).toBe(false);
    expect(actions.canUnblock).toBe(false);
  });

  it('offers only the way back on a frozen voucher', () => {
    expect(ownerActionsForVoucher('blocked')).toEqual({
      canFreezeOrRecall: false,
      canUnblock: true,
    });
  });

  it('matches the status case-insensitively and survives a missing status', () => {
    expect(ownerActionsForVoucher('USED').canFreezeOrRecall).toBe(false);
    expect(ownerActionsForVoucher(undefined).canFreezeOrRecall).toBe(true);
    expect(ownerActionsForVoucher(undefined).canUnblock).toBe(false);
  });
});
describe('groupVouchersByProvider', () => {
  it('groups by brand and totals the litres behind each brand', () => {
    const groups = groupVouchersByProvider([
      mkVoucher({ id: 'a', provider: 'okko', amount: 10 }),
      mkVoucher({ id: 'b', provider: 'WOG', amount: 20 }),
      mkVoucher({ id: 'c', provider: 'OKKO', amount: 5 }),
    ]);

    expect(groups.map((g) => g.provider)).toEqual(['OKKO', 'WOG']);
    expect(groups[0].items.map((v) => v.id)).toEqual(['a', 'c']);
    expect(groups[0].liters).toBe(15);
    expect(groups[1].liters).toBe(20);
  });

  it('keeps a voucher with no provider visible instead of dropping it', () => {
    const groups = groupVouchersByProvider([mkVoucher({ id: 'a', provider: '' })]);
    expect(groups).toHaveLength(1);
    expect(groups[0].provider).toBe('-');
  });
});

describe('buildWorkerRoster', () => {
  const member = (over: Record<string, unknown> = {}) => ({
    id: 'm1',
    workerUserId: 'w1',
    workerFirstName: 'Іван',
    workerLastName: 'Петренко',
    ...over,
  });

  it('counts what the worker holds as active, used and frozen', () => {
    // The API's giftedVoucherCount only counts Assigned vouchers, so it cannot answer
    // "how much is left" — which is the whole point of the roster row.
    const roster = buildWorkerRoster(
      [member()],
      [
        mkVoucher({ id: 'a', workerUserId: 'w1', amount: 10 }),
        mkVoucher({ id: 'b', workerUserId: 'w1', amount: 20 }),
        mkVoucher({ id: 'c', workerUserId: 'w1', amount: 30, status: 'used' }),
        mkVoucher({ id: 'd', workerUserId: 'w1', amount: 40, status: 'blocked' }),
      ],
    );

    expect(roster).toHaveLength(1);
    expect(roster[0]).toMatchObject({
      workerName: 'Іван Петренко',
      activeCount: 2,
      usedCount: 1,
      frozenCount: 1,
      // A frozen voucher is still the worker's, so it stays out of the active litres
      // rather than inflating them.
      activeLiters: 30,
    });
  });

  it('lists a worker holding nothing instead of dropping them from the roster', () => {
    const roster = buildWorkerRoster([member()], []);
    expect(roster[0]).toMatchObject({ activeCount: 0, activeLiters: 0, providers: [] });
  });

  it('ignores company stock, which belongs to no worker', () => {
    const roster = buildWorkerRoster([member()], [mkVoucher({ id: 'pool', workerUserId: null })]);
    expect(roster[0].activeCount).toBe(0);
  });

  it('breaks a worker down by brand for the drill-down', () => {
    const roster = buildWorkerRoster(
      [member()],
      [
        mkVoucher({ id: 'a', workerUserId: 'w1', provider: 'OKKO', amount: 10 }),
        mkVoucher({ id: 'b', workerUserId: 'w1', provider: 'wog', amount: 20 }),
      ],
    );

    expect(roster[0].providers.map((g) => g.provider)).toEqual(['OKKO', 'WOG']);
    expect(roster[0].providers[1].items.map((v) => v.id)).toEqual(['b']);
  });

  it('orders the roster by name so it does not reshuffle on refetch', () => {
    const roster = buildWorkerRoster(
      [
        member({ id: 'm2', workerUserId: 'w2', workerFirstName: 'Андрій', workerLastName: 'Аа' }),
        member({
          id: 'm1',
          workerUserId: 'w1',
          workerFirstName: 'Іван',
          workerLastName: 'Петренко',
        }),
      ],
      [],
    );

    expect(roster.map((r) => r.workerName)).toEqual(['Андрій Аа', 'Іван Петренко']);
  });
});

describe('filterRoster', () => {
  const entry = (name: string, memberId = 'm1') => ({
    memberId,
    workerUserId: 'w1',
    workerName: name,
    activeCount: 0,
    usedCount: 0,
    frozenCount: 0,
    activeLiters: 0,
    providers: [],
  });

  it('matches a name fragment, case-insensitively', () => {
    const roster = [entry('Іван Петренко'), entry('Андрій Аа')];
    expect(filterRoster(roster, 'петр').map((r) => r.workerName)).toEqual(['Іван Петренко']);
    expect(filterRoster(roster, 'ІВАН').map((r) => r.workerName)).toEqual(['Іван Петренко']);
  });

  it('matches a phone number, because that is often all the owner has', () => {
    const roster = [entry('Іван Петренко', 'm1'), entry('Андрій Аа', 'm2')];
    const phones = { m1: '+380501112233', m2: '+380679998877' };

    expect(filterRoster(roster, '5011', phones).map((r) => r.workerName)).toEqual([
      'Іван Петренко',
    ]);
    expect(filterRoster(roster, '+38067999', phones).map((r) => r.workerName)).toEqual([
      'Андрій Аа',
    ]);
  });

  it('returns everything for an empty query rather than nothing', () => {
    const roster = [entry('Іван Петренко')];
    expect(filterRoster(roster, '   ')).toEqual(roster);
  });

  it('returns nothing when nothing matches, so the empty state can say so', () => {
    expect(filterRoster([entry('Іван Петренко')], ' nonexistent')).toEqual([]);
  });
});
