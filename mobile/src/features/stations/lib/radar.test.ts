import {
  availableFuels,
  bestPriceByStation,
  haversineKm,
  packageVoucherPerLiter,
  rankStations,
} from './radar';
import type { FuelPackage, StationNode } from '../../../core/types/api';

function pkg(overrides: Partial<FuelPackage> = {}): FuelPackage {
  return {
    id: 'p',
    stationId: 'okko',
    fuelTypeId: 'ft',
    fuelName: 'А-95',
    liters: 10,
    price: 500,
    originalPrice: 560,
    ...overrides,
  };
}

function node(overrides: Partial<StationNode> = {}): StationNode {
  return {
    id: 'n',
    stationId: 'okko',
    name: 'Node',
    lat: '50.45',
    lng: '30.52',
    ...overrides,
  };
}

describe('haversineKm', () => {
  it('is zero for identical points', () => {
    expect(haversineKm({ lat: 50.45, lng: 30.52 }, { lat: 50.45, lng: 30.52 })).toBe(0);
  });

  it('is ~111 km for one degree of longitude at the equator', () => {
    expect(haversineKm({ lat: 0, lng: 0 }, { lat: 0, lng: 1 })).toBeCloseTo(111.19, 1);
  });

  it('matches the known Kyiv → Lviv distance (~469 km)', () => {
    const km = haversineKm({ lat: 50.4501, lng: 30.5234 }, { lat: 49.8397, lng: 24.0297 });
    expect(km).toBeGreaterThan(460);
    expect(km).toBeLessThan(475);
  });
});

describe('packageVoucherPerLiter', () => {
  it('prefers finalPricePerLiter when present', () => {
    expect(packageVoucherPerLiter(pkg({ finalPricePerLiter: 48.5 }))).toBe(48.5);
  });

  it('falls back to price / liters for legacy rows', () => {
    expect(packageVoucherPerLiter(pkg({ finalPricePerLiter: undefined, price: 500, liters: 10 }))).toBe(50);
  });

  it('returns null when nothing is computable', () => {
    expect(packageVoucherPerLiter(pkg({ finalPricePerLiter: undefined, price: 0, liters: 0 }))).toBeNull();
  });
});

describe('bestPriceByStation', () => {
  it('filters by canonical fuel, keeps the cheapest per station, and computes savings', () => {
    const packages = [
      pkg({ id: 'a', stationId: 'okko', finalPricePerLiter: 50, originalPrice: 560, liters: 10 }),
      pkg({ id: 'b', stationId: 'okko', finalPricePerLiter: 48, originalPrice: 560, liters: 10 }),
      pkg({ id: 'c', stationId: 'wog', fuelName: 'ДП', finalPricePerLiter: 47, originalPrice: 550, liters: 10 }),
    ];
    const map = bestPriceByStation(packages, 'a-95');
    expect(map.size).toBe(1); // ДП excluded
    const okko = map.get('okko')!;
    expect(okko.voucherPerLiter).toBe(48); // cheapest of the two А-95 rows
    expect(okko.pumpPerLiter).toBe(56); // 560 / 10
    expect(okko.savingsPerLiter).toBe(8); // 56 − 48
  });

  it('clamps negative savings to zero', () => {
    const map = bestPriceByStation(
      [pkg({ finalPricePerLiter: 60, originalPrice: 560, liters: 10 })],
      'a-95',
    );
    expect(map.get('okko')!.savingsPerLiter).toBe(0); // pump 56 < voucher 60
  });
});

describe('availableFuels', () => {
  it('returns distinct canonical fuels in first-seen order', () => {
    const packages = [
      pkg({ fuelName: 'А-95' }),
      pkg({ fuelName: 'A-95' }), // same canonical
      pkg({ fuelName: 'ДП' }),
      pkg({ fuelName: 'Газ' }),
    ];
    expect(availableFuels(packages)).toEqual(['a-95', 'diesel', 'gas']);
  });
});

describe('rankStations', () => {
  it('orders priced nodes cheapest-first and pushes unpriced nodes last', () => {
    const nodes = [
      node({ id: 'wog1', stationId: 'wog', name: 'WOG' }),
      node({ id: 'okko1', stationId: 'okko', name: 'OKKO' }),
      node({ id: 'klo1', stationId: 'klo', name: 'KLO' }), // no price
    ];
    const prices = bestPriceByStation(
      [
        pkg({ stationId: 'okko', finalPricePerLiter: 48 }),
        pkg({ stationId: 'wog', finalPricePerLiter: 52 }),
      ],
      'a-95',
    );
    const ranked = rankStations(nodes, prices);
    expect(ranked.map((r) => r.node.id)).toEqual(['okko1', 'wog1', 'klo1']);
    expect(ranked[2].price).toBeNull();
  });

  it('breaks price ties by distance when the user location is known', () => {
    const near = node({ id: 'near', stationId: 'okko', name: 'Near', lat: '50.46', lng: '30.52' });
    const far = node({ id: 'far', stationId: 'okko', name: 'Far', lat: '50.90', lng: '30.52' });
    const prices = bestPriceByStation([pkg({ stationId: 'okko', finalPricePerLiter: 48 })], 'a-95');
    const ranked = rankStations([far, near], prices, { lat: 50.45, lng: 30.52 });
    expect(ranked.map((r) => r.node.id)).toEqual(['near', 'far']);
    expect(ranked[0].distanceKm).toBeGreaterThan(0);
  });
});
