import type { Voucher } from '../../../core/types/api';
import {
  applyListControls,
  availableFacets,
  countActiveFilters,
  EMPTY_FILTERS,
  toggleSort,
  type ListControls,
  type VoucherSortKey,
} from './listControls';

function v(over: Partial<Voucher>): Voucher {
  return {
    id: over.id ?? Math.random().toString(),
    provider: over.provider ?? 'OKKO',
    fuelType: over.fuelType ?? 'a95',
    amount: over.amount ?? 10,
    status: over.status ?? 'active',
    ...over,
  } as Voucher;
}

const NOW = new Date('2026-10-10T12:00:00Z');

describe('applyListControls - filtering', () => {
  const list = [
    v({ id: '1', provider: 'OKKO', fuelType: 'a95', status: 'active', amount: 10 }),
    v({ id: '2', provider: 'WOG', fuelType: 'upg95', status: 'used', amount: 20 }),
    v({ id: '3', provider: 'OKKO', fuelType: 'dp', status: 'active', amount: 30 }),
  ];

  const run = (filters: ListControls['filters']) =>
    applyListControls(list, { sortKey: 'expiry', sortDirection: 'asc', filters }, NOW).map(
      (x) => x.id,
    );

  it('returns everything when nothing is filtered', () => {
    expect(run(EMPTY_FILTERS)).toEqual(['1', '2', '3']);
  });

  it('filters by provider', () => {
    expect(run({ ...EMPTY_FILTERS, provider: 'OKKO' })).toEqual(['1', '3']);
  });

  it('filters by fuel', () => {
    expect(run({ ...EMPTY_FILTERS, fuel: 'dp' })).toEqual(['3']);
  });

  it('filters by status', () => {
    expect(run({ ...EMPTY_FILTERS, status: 'used' })).toEqual(['2']);
  });

  it('combines filters', () => {
    expect(run({ ...EMPTY_FILTERS, provider: 'OKKO', status: 'active' })).toEqual(['1', '3']);
  });

  it('returns nothing when a filter matches nothing', () => {
    expect(run({ ...EMPTY_FILTERS, provider: 'KLO' })).toEqual([]);
  });

  it('does not mutate the caller array', () => {
    const original = [...list];
    applyListControls(
      list,
      { sortKey: 'liters', sortDirection: 'desc', filters: EMPTY_FILTERS },
      NOW,
    );
    expect(list).toEqual(original);
  });
});

describe('applyListControls - hideExpired', () => {
  it('hides a voucher whose date has passed', () => {
    const list = [
      v({ id: 'past', expirationDate: '2026-10-01' }),
      v({ id: 'future', expirationDate: '2026-12-01' }),
    ];
    const kept = applyListControls(
      list,
      { sortKey: 'expiry', sortDirection: 'asc', filters: { hideExpired: true } },
      NOW,
    ).map((x) => x.id);
    expect(kept).toEqual(['future']);
  });

  it('keeps a voucher expiring today', () => {
    const list = [v({ id: 'today', expirationDate: '2026-10-10' })];
    const kept = applyListControls(
      list,
      { sortKey: 'expiry', sortDirection: 'asc', filters: { hideExpired: true } },
      NOW,
    );
    expect(kept).toHaveLength(1);
  });

  it('shows expired vouchers when the filter is off', () => {
    const list = [v({ id: 'past', expirationDate: '2026-10-01' })];
    const kept = applyListControls(
      list,
      { sortKey: 'expiry', sortDirection: 'asc', filters: EMPTY_FILTERS },
      NOW,
    );
    expect(kept).toHaveLength(1);
  });
});

describe('applyListControls - sorting', () => {
  const list = [
    v({ id: 'far', expirationDate: '2026-12-01', amount: 5 }),
    v({ id: 'near', expirationDate: '2026-10-20', amount: 30 }),
    v({ id: 'mid', expirationDate: '2026-11-01', amount: 15 }),
  ];

  it('sorts by expiry soonest first', () => {
    const sorted = applyListControls(
      list,
      { sortKey: 'expiry', sortDirection: 'asc', filters: EMPTY_FILTERS },
      NOW,
    ).map((x) => x.id);
    expect(sorted).toEqual(['near', 'mid', 'far']);
  });

  it('reverses on descending', () => {
    const sorted = applyListControls(
      list,
      { sortKey: 'expiry', sortDirection: 'desc', filters: EMPTY_FILTERS },
      NOW,
    ).map((x) => x.id);
    expect(sorted).toEqual(['far', 'mid', 'near']);
  });

  it('sorts by litres', () => {
    const sorted = applyListControls(
      list,
      { sortKey: 'liters', sortDirection: 'asc', filters: EMPTY_FILTERS },
      NOW,
    ).map((x) => x.id);
    expect(sorted).toEqual(['far', 'mid', 'near']);
  });

  it('sorts by provider', () => {
    const mixed = [
      v({ id: 'w', provider: 'WOG' }),
      v({ id: 'k', provider: 'KLO' }),
      v({ id: 'o', provider: 'OKKO' }),
    ];
    const sorted = applyListControls(
      mixed,
      { sortKey: 'provider', sortDirection: 'asc', filters: EMPTY_FILTERS },
      NOW,
    ).map((x) => x.provider);
    expect(sorted).toEqual(['KLO', 'OKKO', 'WOG']);
  });

  it('puts a voucher with no expiry date last rather than first', () => {
    const withGap = [v({ id: 'none' }), v({ id: 'dated', expirationDate: '2026-11-01' })];
    const sorted = applyListControls(
      withGap,
      { sortKey: 'expiry', sortDirection: 'asc', filters: EMPTY_FILTERS },
      NOW,
    ).map((x) => x.id);
    expect(sorted).toEqual(['dated', 'none']);
  });

  it('breaks ties by expiry so a litres sort still reads chronologically', () => {
    const tied = [
      v({ id: 'late', amount: 10, expirationDate: '2026-12-01' }),
      v({ id: 'early', amount: 10, expirationDate: '2026-10-20' }),
    ];
    const sorted = applyListControls(
      tied,
      { sortKey: 'liters', sortDirection: 'desc', filters: EMPTY_FILTERS },
      NOW,
    ).map((x) => x.id);
    // Equal litres: the tie-break orders by expiry, and the sort direction applies to it too, so
    // descending puts the later expiry first.
    expect(sorted).toEqual(['late', 'early']);
  });
});

describe('toggleSort', () => {
  const base: ListControls = {
    sortKey: 'expiry',
    sortDirection: 'asc',
    filters: EMPTY_FILTERS,
  };

  it('flips direction when the same key is tapped again', () => {
    expect(toggleSort(base, 'expiry').sortDirection).toBe('desc');
    expect(toggleSort(toggleSort(base, 'expiry'), 'expiry').sortDirection).toBe('asc');
  });

  it('starts ascending on a new key', () => {
    expect(toggleSort(base, 'provider')).toMatchObject({
      sortKey: 'provider',
      sortDirection: 'asc',
    });
  });

  it('starts descending for litres, where the biggest tank is the useful first view', () => {
    expect(toggleSort(base, 'liters')).toMatchObject({ sortKey: 'liters', sortDirection: 'desc' });
  });
});

describe('availableFacets', () => {
  it('offers only providers and fuels actually present', () => {
    const facets = availableFacets([
      v({ provider: 'WOG', fuelType: 'upg95' }),
      v({ provider: 'OKKO', fuelType: 'a95' }),
      v({ provider: 'OKKO', fuelType: 'dp' }),
    ]);
    expect(facets.providers).toEqual(['OKKO', 'WOG']);
    expect(facets.fuels).toEqual(['a95', 'dp', 'upg95']);
  });

  it('returns empty lists for no vouchers', () => {
    expect(availableFacets([])).toEqual({ providers: [], fuels: [] });
  });
});

describe('countActiveFilters', () => {
  const keys: VoucherSortKey[] = ['expiry', 'provider', 'fuel', 'liters'];
  expect(keys).toHaveLength(4);

  it('counts nothing when nothing is set', () => {
    expect(countActiveFilters(EMPTY_FILTERS)).toBe(0);
  });

  it('counts each active filter', () => {
    expect(
      countActiveFilters({ provider: 'OKKO', fuel: 'a95', status: 'active', hideExpired: true }),
    ).toBe(4);
    expect(countActiveFilters({ hideExpired: true })).toBe(1);
  });
});
