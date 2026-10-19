import { wireBrands, wireBrandStations, wireFuelTypes, wirePackages } from './wireFixtures';
import type { FuelPackage, FuelType, Station, StationNode } from './api';

/**
 * The wire contract between the API and this app, asserted against responses
 * captured from production (see `wireFixtures.ts`).
 *
 * WHY THIS EXISTS
 * On 2026-10-18 the server renamed `station_id` to `brand_id` across the catalogue.
 * The client's URLs were changed in that commit; the field it read was not. Every
 * АЗК arrived with `brandId` and no `stationId`, so the map lost its logos and the
 * network leaderboard threw `undefined.localeCompare` on open — in front of users,
 * because the fix shipped by OTA. Nothing caught it: the unit tests built their
 * fixtures from the client's own interfaces, so the client and its tests agreed
 * with each other and neither was checked against the controller.
 *
 * That is the failure this file is shaped to catch. It is not a type test — a
 * `JSON.parse` fixture satisfies any interface at compile time, which is precisely
 * why the drift was invisible. It compares the **key sets and value types actually
 * on the wire** against what this client reads.
 *
 * WHAT IT COSTS
 * A deliberate API change fails here until the fixture and the expected list are
 * updated in the same commit. That is the intended price: the two must move
 * together, and the reviewer sees both. An *accidental* rename fails here on the
 * server side alone, with no client change to make it pass — which is the case
 * that cost a day.
 */

type Kind = 'string' | 'number' | 'boolean' | 'object' | 'null';

const kindOf = (value: unknown): Kind => {
  if (value === null) return 'null';
  if (Array.isArray(value)) return 'object';
  return typeof value as Kind;
};

/**
 * Assert one captured record against the shape this client reads.
 *
 * `expected` is `{ key: Kind | Kind[] }`. An array means the server sends any one
 * of those — a nullable column like `city`, which is `null` for a street-side
 * АЗК and a string for a town one. Collapsing that to one kind means asserting a
 * property of the first fixture record instead of the endpoint, which is the same
 * mistake this file exists to catch.
 */
function expectShape(
  label: string,
  record: Record<string, unknown>,
  expected: Record<string, Kind | Kind[]>,
  optional: string[] = [],
) {
  const actual = Object.keys(record).sort();

  expect({ [label]: actual }).toEqual({
    [label]: [...Object.keys(expected), ...optional].sort(),
  });

  for (const [key, allowed] of Object.entries(expected)) {
    if (!(key in record)) {
      throw new Error(`${label}: the server did not send "${key}" at all`);
    }
    const kinds = Array.isArray(allowed) ? allowed : [allowed];
    if (!kinds.includes(kindOf(record[key]))) {
      throw new Error(
        `${label}.${key} is ${kindOf(record[key])}, expected one of ${kinds.join(' | ')}`,
      );
    }
  }
}

describe('GET /api/brands — Station[]', () => {
  it('sends exactly the keys Station reads', () => {
    for (const brand of wireBrands) {
      expectShape('brand', brand, {
        id: 'string',
        slug: 'string',
        name: 'string',
        color: 'string',
        logoText: 'string',
        sortOrder: 'number',
      });
    }
  });

  it('is the shape the app actually consumes', () => {
    const first: Station = wireBrands[0];
    expect(first.slug).toBe('okko');
  });
});

describe('GET /api/brand-stations — StationNode[]', () => {
  it('sends exactly the keys StationNode reads', () => {
    for (const node of wireBrandStations) {
      expectShape('node', node, {
        id: 'string',
        brandId: 'string',
        brandSlug: 'string',
        name: 'string',
        address: ['string', 'null'],
        phone: ['string', 'null'],
        city: ['string', 'null'],
        stationType: ['string', 'null'],
        lat: 'number',
        lng: 'number',
      });
    }
  });

  it('the client type declares the same lat/lng the server sends', () => {
    // The strongest assertion in this file, because it is the only one TypeScript
    // checks: assigning a captured record to the interface the app uses fails to
    // compile when a declared field's type drifts from the wire. Everything else
    // here is a runtime string comparison and could be forgotten.
    const node: StationNode = wireBrandStations[0];
    expect(typeof node.lat).toBe('number');
  });

  it('gives every node the brand slug the logo palette is keyed by', () => {
    // `BRAND_LOGOS` / `BRAND_COLORS` are keyed by slug. A node carrying only the
    // uuid is what made the map draw 1300 unbranded pins.
    for (const node of wireBrandStations) {
      expect(typeof node.brandSlug).toBe('string');
      expect(node.brandSlug.length).toBeGreaterThan(0);
    }
  });
});

describe('GET /api/brands/fuel-types — FuelType[]', () => {
  it('sends exactly the keys FuelType reads', () => {
    for (const fuel of wireFuelTypes) {
      expectShape('fuel', fuel, {
        id: 'string',
        brandId: 'string',
        brandSlug: 'string',
        name: 'string',
        basePrice: 'number',
        discountPrice: 'number',
        // The column price, our own price, and whether the shop can sell it. The first two are
        // nullable - a fuel nobody has priced has neither - and the third is what tells the client
        // which of the two prices is the one to show.
        pumpPricePerLiter: ['number', 'null'],
        finalPricePerLiter: ['number', 'null'],
        isAvailable: 'boolean',
      });
    }
  });
});

describe('GET /api/packages — FuelPackage[]', () => {
  it('sends exactly the keys FuelPackage reads', () => {
    for (const pkg of wirePackages) {
      expectShape('package', pkg, {
        id: 'string',
        brandId: 'string',
        brandSlug: 'string',
        fuelTypeId: 'string',
        fuelName: 'string',
        liters: 'number',
        price: 'number',
        originalPrice: 'number',
        finalPricePerLiter: 'number',
        isAvailable: 'boolean',
      });
    }
  });

  it('is the shape the app actually consumes', () => {
    const first: FuelPackage = wirePackages[0];
    expect(first.brandId).toBe(wireBrands[0].id);
  });
});

describe('the catalogue joins on brandId, end to end', () => {
  // The regression that reached users: the server sends `brandId`, and every join
  // in the app compares against it. If a rename makes either side drift, this is
  // where it shows — no device required.
  it('every fuel type points at a brand the brands endpoint returned', () => {
    const ids = new Set(wireBrands.map((b) => b.id));
    for (const fuel of wireFuelTypes) {
      expect(ids.has(fuel.brandId)).toBe(true);
    }
  });

  it('every package points at a brand the brands endpoint returned', () => {
    const ids = new Set(wireBrands.map((b) => b.id));
    for (const pkg of wirePackages) {
      expect(ids.has(pkg.brandId)).toBe(true);
    }
  });

  it('every АЗК points at a brand the brands endpoint returned', () => {
    const ids = new Set(wireBrands.map((b) => b.id));
    for (const node of wireBrandStations) {
      expect(ids.has(node.brandId)).toBe(true);
    }
  });

  it('the slug the palette keys on is the same slug the server sent', () => {
    const byId = new Map(wireBrands.map((b) => [b.id, b.slug]));
    for (const node of wireBrandStations) {
      expect(node.brandSlug).toBe(byId.get(node.brandId));
    }
    for (const pkg of wirePackages) {
      expect(pkg.brandSlug).toBe(byId.get(pkg.brandId));
    }
  });
});
