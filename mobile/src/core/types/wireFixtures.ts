// ---------------------------------------------------------------------------
// CAPTURED, NOT WRITTEN BY HAND. Every record below is a verbatim slice of a live
// production response, taken 2026-10-19 from https://api.palne.shop:
//
//   wireBrands       <- GET /api/brands
//   wireBrandStations<- GET /api/brand-stations
//   wireFuelTypes    <- GET /api/brands/fuel-types
//   wirePackages     <- GET /api/packages
//
// The first four records of each, unchanged.
//
// WHY CAPTURED. The client and the server drifted apart on 2026-10-18 — the server
// renamed `station_id` to `brand_id` and the client kept reading the old name —
// and every test stayed green, because the test fixtures were written from the
// CLIENT's types. A fixture that agrees with the code cannot catch the code
// disagreeing with the server. These are the server's words.
//
// To refresh after an intentional API change: re-capture, update the expected key
// lists in `wireContract.test.ts` in the same commit, and read the diff. A refresh
// that only touches this file is a test nobody looked at.
//
// ONE EXCEPTION, THE BRAND IDS. The four brand ids below are the ones the server
// carries after `20261019201500_SeededBrandIdsBecomeDerived` gave the seeded networks
// ids derived from their slug; the capture of 2026-10-19 carried the constants that
// migration replaced. They are values, not shape - the client treats a brand id as
// opaque - but a fixture that says "captured" should not carry a value the server
// no longer sends.
// ---------------------------------------------------------------------------

/** GET /api/brands — `Station` on the client, `PublicBrandResponse` on the server. */
export const wireBrands = [
  {
    id: '89a11944-bdb3-5ed4-b55c-edae31335a6e',
    slug: 'okko',
    name: 'OKKO',
    color: '#22c55e',
    logoText: 'OKKO',
    sortOrder: 1,
  },
  {
    id: '0b9d7319-8ce3-50c9-a6e0-58e850217ffa',
    slug: 'wog',
    name: 'WOG',
    color: '#10b981',
    logoText: 'WOG',
    sortOrder: 2,
  },
  {
    id: '080a78c0-c80b-53f1-97cd-0fedb0ea953b',
    slug: 'upg',
    name: 'UPG',
    color: '#06b6d4',
    logoText: 'UPG',
    sortOrder: 3,
  },
  {
    id: '48652f56-ca2d-590e-acfd-e8d9f970433b',
    slug: 'klo',
    name: 'KLO',
    color: '#eab308',
    logoText: 'KLO',
    sortOrder: 4,
  },
];

/** GET /api/brand-stations — `StationNode` on the client. */
export const wireBrandStations = [
  {
    id: 'b889b58c-7cea-4203-b43f-a6450fa9dbb4',
    brandId: '48652f56-ca2d-590e-acfd-e8d9f970433b',
    brandSlug: 'klo',
    name: 'KLO',
    address: 'вулиця Семена Скляренка, 5Б',
    phone: '800 301 555',
    city: null,
    lat: 50.4969698,
    lng: 30.4645316,
  },
  {
    id: '4ab83747-5b14-4a2e-a4f5-8f2c86c03cfe',
    brandId: '48652f56-ca2d-590e-acfd-e8d9f970433b',
    brandSlug: 'klo',
    name: 'KLO Ірпінь',
    address: 'вулиця Соборна, 195-3',
    phone: '800 301 555',
    city: 'Ірпінь',
    lat: 50.53647237,
    lng: 30.23198009,
  },
  {
    id: '28de24c5-7587-4995-9308-f031221703da',
    brandId: '48652f56-ca2d-590e-acfd-e8d9f970433b',
    brandSlug: 'klo',
    name: 'KLO Ірпінь',
    address: 'вулиця Соборна, 195-3',
    phone: '800 301 555',
    city: 'Ірпінь',
    lat: 50.5365717,
    lng: 30.23208033,
  },
  {
    id: '865196ca-7336-4608-af8e-4a1c51a7aa6c',
    brandId: '48652f56-ca2d-590e-acfd-e8d9f970433b',
    brandSlug: 'klo',
    name: 'KLO Березань',
    address: 'проспект Володимира Маяковського, 10',
    phone: '800 301 555',
    city: 'Березань',
    lat: 50.32462407,
    lng: 31.51333837,
  },
];

/** GET /api/brands/fuel-types — `FuelType` on the client. */
export const wireFuelTypes = [
  {
    id: '01a15198-201d-7968-b669-0a51ecafb7ef',
    brandId: '0b9d7319-8ce3-50c9-a6e0-58e850217ffa',
    brandSlug: 'wog',
    name: '100 Mustang Євро5-Е0',
    basePrice: 102.9,
    discountPrice: 102.9,
    pumpPricePerLiter: 102.9,
    finalPricePerLiter: 102.9,
    isAvailable: false,
  },
  {
    id: '01a13e35-3153-7682-92ec-e513e60dcbe4',
    brandId: '89a11944-bdb3-5ed4-b55c-edae31335a6e',
    brandSlug: 'okko',
    name: '100 PULLS',
    basePrice: 103.4,
    discountPrice: 102.9,
    pumpPricePerLiter: 103.4,
    finalPricePerLiter: 102.9,
    isAvailable: false,
  },
  {
    id: '01a15198-2018-7cad-80ad-b02e167ffd22',
    brandId: '0b9d7319-8ce3-50c9-a6e0-58e850217ffa',
    brandSlug: 'wog',
    name: '95 Mustang Євро5-Е10',
    basePrice: 95.9,
    discountPrice: 95.9,
    pumpPricePerLiter: 95.9,
    finalPricePerLiter: 95.9,
    isAvailable: false,
  },
  {
    id: 'c7fae808-d061-4929-8e78-2bf8e19e40a1',
    brandId: '0b9d7319-8ce3-50c9-a6e0-58e850217ffa',
    brandSlug: 'wog',
    name: '95 Євро5-Е10',
    basePrice: 92.9,
    discountPrice: 92.9,
    pumpPricePerLiter: 92.9,
    finalPricePerLiter: 92.9,
    isAvailable: false,
  },
];

/** GET /api/packages — `FuelPackage` on the client. */
export const wirePackages = [
  {
    id: '01a13e35-3153-7682-92ec-e513e60dcbe4:2',
    brandId: '89a11944-bdb3-5ed4-b55c-edae31335a6e',
    brandSlug: 'okko',
    fuelTypeId: '01a13e35-3153-7682-92ec-e513e60dcbe4',
    fuelName: '100 PULLS',
    liters: 2.0,
    price: 205.8,
    originalPrice: 205.8,
    finalPricePerLiter: 102.9,
    isAvailable: false,
  },
  {
    id: '01a13e35-3153-7682-92ec-e513e60dcbe4:3',
    brandId: '89a11944-bdb3-5ed4-b55c-edae31335a6e',
    brandSlug: 'okko',
    fuelTypeId: '01a13e35-3153-7682-92ec-e513e60dcbe4',
    fuelName: '100 PULLS',
    liters: 3.0,
    price: 308.7,
    originalPrice: 308.7,
    finalPricePerLiter: 102.9,
    isAvailable: false,
  },
  {
    id: '01a13e35-3153-7682-92ec-e513e60dcbe4:5',
    brandId: '89a11944-bdb3-5ed4-b55c-edae31335a6e',
    brandSlug: 'okko',
    fuelTypeId: '01a13e35-3153-7682-92ec-e513e60dcbe4',
    fuelName: '100 PULLS',
    liters: 5.0,
    price: 514.5,
    originalPrice: 514.5,
    finalPricePerLiter: 102.9,
    isAvailable: false,
  },
  {
    id: '01a13e35-3153-7682-92ec-e513e60dcbe4:10',
    brandId: '89a11944-bdb3-5ed4-b55c-edae31335a6e',
    brandSlug: 'okko',
    fuelTypeId: '01a13e35-3153-7682-92ec-e513e60dcbe4',
    fuelName: '100 PULLS',
    liters: 10.0,
    price: 1029.0,
    originalPrice: 1029.0,
    finalPricePerLiter: 102.9,
    isAvailable: false,
  },
];
