import {
  availableFuels,
  bestPriceByStation,
  formatShortAddress,
  haversineKm,
  packageVoucherPerLiter,
  radarWithinRadius,
  rankBrands,
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
    expect(
      packageVoucherPerLiter(pkg({ finalPricePerLiter: undefined, price: 500, liters: 10 })),
    ).toBe(50);
  });

  it('returns null when nothing is computable', () => {
    expect(
      packageVoucherPerLiter(pkg({ finalPricePerLiter: undefined, price: 0, liters: 0 })),
    ).toBeNull();
  });
});

describe('bestPriceByStation', () => {
  it('filters by canonical fuel, keeps the cheapest per station, and computes savings', () => {
    const packages = [
      pkg({ id: 'a', stationId: 'okko', finalPricePerLiter: 50, originalPrice: 560, liters: 10 }),
      pkg({ id: 'b', stationId: 'okko', finalPricePerLiter: 48, originalPrice: 560, liters: 10 }),
      pkg({
        id: 'c',
        stationId: 'wog',
        fuelName: 'ДП',
        finalPricePerLiter: 47,
        originalPrice: 550,
        liters: 10,
      }),
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

describe('radarWithinRadius', () => {
  // User at (50, 30); nodes offset in latitude only, so distance ≈ 111 km × Δlat.
  const user = { lat: 50, lng: 30 };
  const prices = bestPriceByStation([pkg({ stationId: 'okko', finalPricePerLiter: 48 })], 'a-95');
  const rank = (nodes: StationNode[], loc = user) => rankStations(nodes, prices, loc);

  it('returns every priced station with a null radius when the location is unknown', () => {
    const nodes = [
      node({ id: 'a', stationId: 'okko', lat: '50.036', lng: '30' }),
      node({ id: 'x', stationId: 'klo', lat: '50.036', lng: '30' }), // unpriced
    ];
    const res = radarWithinRadius(rankStations(nodes, prices, null), null);
    expect(res.radiusKm).toBeNull();
    expect(res.stations.map((r) => r.node.id)).toEqual(['a']); // unpriced dropped
  });

  it('expands to the smallest ring that holds at least minResults priced stations', () => {
    const nodes = [
      node({ id: 'a', lat: '50.036', lng: '30' }), // ~4 km
      node({ id: 'b', lat: '50.072', lng: '30' }), // ~8 km
      node({ id: 'c', lat: '50.36', lng: '30' }), //  ~40 km
      node({ id: 'd', lat: '51.35', lng: '30' }), //  ~150 km
    ];
    const res = radarWithinRadius(rank(nodes), user); // minResults defaults to 3
    expect(res.radiusKm).toBe(50);
    expect(res.stations.map((r) => r.node.id)).toEqual(['a', 'b', 'c']);
  });

  it('picks the tightest ring that already satisfies a smaller minResults', () => {
    const nodes = [
      node({ id: 'a', lat: '50.036', lng: '30' }), // ~4 km
      node({ id: 'b', lat: '50.36', lng: '30' }), //  ~40 km
    ];
    const res = radarWithinRadius(rank(nodes), user, 1);
    expect(res.radiusKm).toBe(5);
    expect(res.stations.map((r) => r.node.id)).toEqual(['a']);
  });

  it('shows all priced stations (null radius) when none fall within the widest ring', () => {
    const nodes = [node({ id: 'd', lat: '51.35', lng: '30' })]; // ~150 km
    const res = radarWithinRadius(rank(nodes), user);
    expect(res.radiusKm).toBeNull();
    expect(res.stations.map((r) => r.node.id)).toEqual(['d']);
  });
});

describe('rankBrands', () => {
  const user = { lat: 50, lng: 30 };
  const prices = bestPriceByStation(
    [
      pkg({ stationId: 'okko', finalPricePerLiter: 48, originalPrice: 560, liters: 10 }),
      pkg({ stationId: 'wog', finalPricePerLiter: 46, originalPrice: 560, liters: 10 }),
    ],
    'a-95',
  );

  it('collapses nodes into one row per brand, cheapest voucher грн/л first', () => {
    const nodes = [
      node({ id: 'okko-near', stationId: 'okko', lat: '50.036', lng: '30' }), // ~4 km
      node({ id: 'okko-far', stationId: 'okko', lat: '50.36', lng: '30' }), //  ~40 km
      node({ id: 'wog-1', stationId: 'wog', lat: '50.09', lng: '30' }), //    ~10 km
    ];
    const brands = rankBrands(rankStations(nodes, prices, user));
    expect(brands.map((b) => b.stationId)).toEqual(['wog', 'okko']); // 46 before 48
    const okko = brands.find((b) => b.stationId === 'okko')!;
    expect(okko.nodes.map((n) => n.node.id)).toEqual(['okko-near', 'okko-far']); // nearest-first
    expect(okko.nearestDistanceKm).toBeCloseTo(okko.nodes[0].distanceKm!, 5);
  });

  it('ignores unpriced brands — the leaderboard is a price ranking', () => {
    const nodes = [
      node({ id: 'okko-1', stationId: 'okko', lat: '50.036', lng: '30' }),
      node({ id: 'klo-1', stationId: 'klo', lat: '50.036', lng: '30' }), // no price
    ];
    const brands = rankBrands(rankStations(nodes, prices, user));
    expect(brands.map((b) => b.stationId)).toEqual(['okko']);
  });

  // A brand with nothing inside the ring used to vanish from the leaderboard entirely, which
  // read as "this network doesn't exist" rather than "its pumps are 75 km away" — the shape a
  // Lviv customer saw for KLO, whose nearest АЗК is 75 km from the city.
  describe('keeps an out-of-range network visible', () => {
    const threeBrands = bestPriceByStation(
      [
        pkg({ stationId: 'okko', finalPricePerLiter: 48 }),
        pkg({ stationId: 'wog', finalPricePerLiter: 46 }),
        pkg({ stationId: 'klo', finalPricePerLiter: 44 }), // cheapest overall, but far away
      ],
      'a-95',
    );
    const nodes = [
      node({ id: 'okko-1', stationId: 'okko', lat: '50.036', lng: '30' }), //  ~4 km
      node({ id: 'okko-far', stationId: 'okko', lat: '50.36', lng: '30' }), // ~40 km
      node({ id: 'wog-1', stationId: 'wog', lat: '50.09', lng: '30' }), //   ~10 km
      node({ id: 'klo-1', stationId: 'klo', lat: '51.4', lng: '30' }), //  ~155 km
    ];
    const ranked = rankStations(nodes, threeBrands, user);

    it('sorts a cheaper but unreachable network below every reachable one', () => {
      const brands = rankBrands(ranked, 20);
      expect(brands.map((b) => b.stationId)).toEqual(['wog', 'okko', 'klo']);
      expect(brands[0].inRange).toBe(true);
      expect(brands[2].inRange).toBe(false);
    });

    it('reports the true nearest distance, not a radius-clipped one', () => {
      const klo = rankBrands(ranked, 20).find((b) => b.stationId === 'klo')!;
      expect(klo.nearestDistanceKm).toBeGreaterThan(100);
      expect(klo.nearestDistanceKm).toBeCloseTo(klo.nodes[0].distanceKm!, 5);
    });

    it('lists the in-radius АЗК for a reachable brand so its drill-in is unchanged', () => {
      const okko = rankBrands(ranked, 20).find((b) => b.stationId === 'okko')!;
      const all = rankBrands(ranked).find((b) => b.stationId === 'okko')!;
      expect(okko.nodes.map((n) => n.node.id)).toEqual(['okko-1']);
      // The unbounded call has no radius to clip against, so it keeps the whole brand.
      expect(all.nodes.map((n) => n.node.id)).toEqual(['okko-1', 'okko-far']);
    });

    it('falls back to the nearest-first list for an unreachable brand, so the drill-in can answer how far', () => {
      const klo = rankBrands(ranked, 20).find((b) => b.stationId === 'klo')!;
      expect(klo.nodes.map((n) => n.node.id)).toEqual(['klo-1']);
    });

    it('treats every brand as in range when the radius is unbounded', () => {
      const brands = rankBrands(ranked); // no radius: location unknown, or nothing met the bar
      expect(brands.every((b) => b.inRange)).toBe(true);
      expect(brands.map((b) => b.stationId)).toEqual(['klo', 'wog', 'okko']); // 44, 46, 48
    });
  });
});

describe('formatShortAddress', () => {
  it('strips the OKKO site code and prepends the city', () => {
    expect(
      formatShortAddress({ city: 'Івано-Франківськ', address: 'вул. Хриплинська, 9 АЗК №01' }),
    ).toBe('Івано-Франківськ, вул. Хриплинська, 9');
  });

  it('drops oblast, raion and the settlement from a verbose WOG address', () => {
    expect(
      formatShortAddress({
        city: 'Ізмаїл',
        address: 'Одеська область, Ізмаїльський район, м.Ізмаїл, пр.Незалежності, 378',
      }),
    ).toBe('Ізмаїл, пр.Незалежності, 378');
  });

  it('drops the country and oblast abbreviation', () => {
    expect(
      formatShortAddress({
        city: 'Буча',
        address: 'Україна, Київська обл., м. Буча, вул. Нове шосе, 81',
      }),
    ).toBe('Буча, вул. Нове шосе, 81');
  });

  // UPG is imported already stripped — oblast and raion removed by the fetch script, matching the
  // short OKKO shape — so these cover the two UPG forms the formatter must leave alone: a roadside
  // site with no settlement, and the seven sites whose settlement is glued to the street.
  it('leaves a UPG roadside site with no city intact', () => {
    expect(
      formatShortAddress({
        city: undefined,
        address: 'с/рада Загальцівська, автошлях Київ-Ковель-Ягодин, 65 км + 700 м',
      }),
    ).toBe('с/рада Загальцівська, автошлях Київ-Ковель-Ягодин, 65 км + 700 м');
  });

  it('leaves a UPG address whose settlement is glued to the street intact', () => {
    expect(
      formatShortAddress({
        city: undefined,
        address: 'м. Корсунь-Шевченківський вул. Гіфхорнська 25',
      }),
    ).toBe('Корсунь-Шевченківський вул. Гіфхорнська 25');
  });

  it('prepends the city to a short UPG street address', () => {
    expect(
      formatShortAddress({ city: 'Запоріжжя', address: 'вул. професора Анатолія Бойка, 3' }),
    ).toBe('Запоріжжя, вул. професора Анатолія Бойка, 3');
  });

  it('derives the city from the settlement segment when the node has none', () => {
    expect(
      formatShortAddress({
        city: undefined,
        address: 'м. Київ, Шевченківський район, вул.Юрія Іллєнка, 50',
      }),
    ).toBe('Київ, вул.Юрія Іллєнка, 50');
  });

  it('strips a буд. prefix from the house number', () => {
    expect(
      formatShortAddress({ city: 'Борислав', address: 'м.Борислав, вул.Коваліва, буд.46а' }),
    ).toBe('Борислав, вул.Коваліва, 46а');
  });

  it('strips a misspelt oblast token (облсть, no а)', () => {
    expect(
      formatShortAddress({ city: 'Львів', address: 'Львів, Львівська облсть, вул.Стуса В., 57 а' }),
    ).toBe('Львів, вул.Стуса В., 57 а');
  });

  it('drops a numberless б/н prefix so the crossing street survives', () => {
    expect(
      formatShortAddress({ city: 'Львів', address: 'Львів, вул. Луганська, б/н / вул. Стрийська' }),
    ).toBe('Львів, вул. Луганська, вул. Стрийська');
  });

  it('falls back to the city alone when there is no address', () => {
    expect(formatShortAddress({ city: 'Львів', address: undefined })).toBe('Львів');
  });

  it('returns an empty string when nothing is usable', () => {
    expect(formatShortAddress({ city: undefined, address: undefined })).toBe('');
  });
});
