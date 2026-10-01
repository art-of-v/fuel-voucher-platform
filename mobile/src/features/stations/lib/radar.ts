import { normalizeFuelName } from '../../../core/utils/formatters';
import type { FuelPackage, StationNode } from '../../../core/types/api';

export interface LatLng {
  lat: number;
  lng: number;
}

export interface StationPrice {
  /** Cheapest final voucher price per litre across the brand's packages for the fuel. */
  voucherPerLiter: number;
  /** Pump (list) price per litre — the struck-through reference. */
  pumpPerLiter: number;
  /** `pumpPerLiter − voucherPerLiter`, clamped to ≥ 0. */
  savingsPerLiter: number;
}

const EARTH_RADIUS_KM = 6371;

function toRad(deg: number): number {
  return (deg * Math.PI) / 180;
}

/** Great-circle distance in kilometres between two coordinates. */
export function haversineKm(a: LatLng, b: LatLng): number {
  const dLat = toRad(b.lat - a.lat);
  const dLng = toRad(b.lng - a.lng);
  const lat1 = toRad(a.lat);
  const lat2 = toRad(b.lat);
  const h =
    Math.sin(dLat / 2) ** 2 + Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLng / 2) ** 2;
  return 2 * EARTH_RADIUS_KM * Math.asin(Math.min(1, Math.sqrt(h)));
}

/**
 * Per-package voucher price per litre. `finalPricePerLiter` (грн/л) is authoritative;
 * legacy rows that predate the pricing model carry no such value, so we fall back to
 * the package total ÷ litres. Returns null when neither can be computed.
 */
export function packageVoucherPerLiter(pkg: FuelPackage): number | null {
  if (typeof pkg.finalPricePerLiter === 'number' && pkg.finalPricePerLiter > 0) {
    return pkg.finalPricePerLiter;
  }
  if (pkg.liters > 0 && pkg.price > 0) return pkg.price / pkg.liters;
  return null;
}

/** Pump (list) price per litre, derived from the struck-through package total. */
export function packagePumpPerLiter(pkg: FuelPackage): number | null {
  if (pkg.liters > 0 && pkg.originalPrice > 0) return pkg.originalPrice / pkg.liters;
  return null;
}

/**
 * Cheapest voucher грн/л per station (brand) for the given canonical fuel. Packages are
 * per-brand and keyed by `stationId`, so every node of a brand shares this price. The
 * fuel argument is a canonical id from {@link normalizeFuelName} (e.g. `'a-95'`).
 */
export function bestPriceByStation(
  packages: FuelPackage[],
  canonicalFuel: string,
): Map<string, StationPrice> {
  const byStation = new Map<string, StationPrice>();
  for (const pkg of packages) {
    if (normalizeFuelName(pkg.fuelName) !== canonicalFuel) continue;
    const voucher = packageVoucherPerLiter(pkg);
    if (voucher == null) continue;
    const existing = byStation.get(pkg.stationId);
    if (existing && existing.voucherPerLiter <= voucher) continue;
    const pump = packagePumpPerLiter(pkg);
    byStation.set(pkg.stationId, {
      voucherPerLiter: voucher,
      pumpPerLiter: pump ?? voucher,
      savingsPerLiter: pump != null ? Math.max(0, pump - voucher) : 0,
    });
  }
  return byStation;
}

/** Distinct canonical fuels present in the packages, in first-seen order. */
export function availableFuels(packages: FuelPackage[]): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const pkg of packages) {
    const fuel = normalizeFuelName(pkg.fuelName);
    if (!seen.has(fuel)) {
      seen.add(fuel);
      out.push(fuel);
    }
  }
  return out;
}

export interface RankedStation {
  node: StationNode;
  price: StationPrice | null;
  distanceKm: number | null;
}

function nodeLatLng(node: StationNode): LatLng | null {
  const lat = parseFloat(node.lat ?? '');
  const lng = parseFloat(node.lng ?? '');
  if (!Number.isFinite(lat) || !Number.isFinite(lng) || (lat === 0 && lng === 0)) return null;
  return { lat, lng };
}

/**
 * Ranks АЗК nodes cheapest-voucher-грн/л first — the radar's primary sort. Nodes with a
 * price for the selected fuel come before those without; within a group, ties break by
 * distance when the user's location is known, then by name. Every node carries its
 * distance (when locatable) so the UI can display it.
 */
export function rankStations(
  nodes: StationNode[],
  priceByStation: Map<string, StationPrice>,
  userLoc?: LatLng | null,
): RankedStation[] {
  const ranked: RankedStation[] = nodes.map((node) => {
    const price = priceByStation.get(node.stationId) ?? null;
    let distanceKm: number | null = null;
    if (userLoc) {
      const coord = nodeLatLng(node);
      if (coord) distanceKm = haversineKm(userLoc, coord);
    }
    return { node, price, distanceKm };
  });

  ranked.sort((a, b) => {
    const aPriced = a.price != null;
    const bPriced = b.price != null;
    if (aPriced !== bPriced) return aPriced ? -1 : 1;
    if (a.price && b.price && a.price.voucherPerLiter !== b.price.voucherPerLiter) {
      return a.price.voucherPerLiter - b.price.voucherPerLiter;
    }
    // Same price (or both unpriced): nearest first when distance is known.
    if (a.distanceKm != null && b.distanceKm != null && a.distanceKm !== b.distanceKm) {
      return a.distanceKm - b.distanceKm;
    }
    if (a.distanceKm != null && b.distanceKm == null) return -1;
    if (a.distanceKm == null && b.distanceKm != null) return 1;
    return a.node.name.localeCompare(b.node.name);
  });

  return ranked;
}

/** Radius rings (km) the radar widens through until it finds enough priced АЗК. */
export const RADIUS_LADDER_KM = [5, 10, 20, 50, 100] as const;

export interface RadarResult {
  /** Priced stations to show, cheapest-voucher-грн/л first (order inherited from the input). */
  stations: RankedStation[];
  /** The ring (km) these results sit within, or null when unbounded (see below). */
  radiusKm: number | null;
}

/**
 * Applies the radius auto-expand ladder to a ranked list — the heart of "nearby". Walks
 * {@link RADIUS_LADDER_KM} and returns the priced stations inside the first ring that holds
 * at least `minResults` of them, so a cheaper АЗК on the far side of the country can't
 * outrank the pumps actually near the user.
 *
 * Two cases yield an unbounded result (`radiusKm: null`, every priced station returned so
 * the list is never needlessly empty):
 *  - the user's location is unknown (no distance to measure → rank by price only, the
 *    pre-ladder behaviour);
 *  - no ring up to the widest reaches `minResults` (the nearest priced АЗК are all far).
 *
 * Unpriced nodes are dropped — this list is a price ranking. Input order is preserved.
 */
export function radarWithinRadius(
  ranked: RankedStation[],
  userLoc?: LatLng | null,
  minResults = 3,
  ladder: readonly number[] = RADIUS_LADDER_KM,
): RadarResult {
  const priced = ranked.filter((r) => r.price != null);
  if (!userLoc) return { stations: priced, radiusKm: null };
  for (const radiusKm of ladder) {
    const within = priced.filter((r) => r.distanceKm != null && r.distanceKm <= radiusKm);
    if (within.length >= minResults) return { stations: within, radiusKm };
  }
  return { stations: priced, radiusKm: null };
}

export interface BrandRank {
  /** Brand id (= `stationId`), e.g. `'wog'`. The leaderboard key. */
  stationId: string;
  /** Voucher грн/л for the fuel. Packages are per-brand, so every node shares it. */
  price: StationPrice;
  /** Distance to this brand's nearest АЗК in the set, or null when location is unknown. */
  nearestDistanceKm: number | null;
  /** This brand's АЗК, nearest-first — the drill-in list behind a leaderboard row. */
  nodes: RankedStation[];
}

/**
 * Collapses a ranked station list into a NETWORK leaderboard: one row per brand, cheapest
 * voucher грн/л first — the view the customer asked for ("лідер по найнижчій ціні —
 * WOG, друге OKKO…"). A brand's many nearby pumps share one voucher price, so showing each
 * node individually buried that comparison; here each brand appears once, and its nodes are
 * carried nearest-first so a tap can drill into "how far is the closest WOG?".
 *
 * Expects an already-priced, already-ranked list (e.g. {@link RadarResult.stations}); any
 * unpriced entry is ignored, since the leaderboard is a price ranking. Brands tie-break by
 * their nearest АЗК's distance, then by id, matching {@link rankStations}.
 */
export function rankBrands(ranked: RankedStation[]): BrandRank[] {
  const byBrand = new Map<string, RankedStation[]>();
  for (const r of ranked) {
    if (r.price == null) continue;
    const list = byBrand.get(r.node.stationId);
    if (list) list.push(r);
    else byBrand.set(r.node.stationId, [r]);
  }

  const brands: BrandRank[] = [];
  for (const [stationId, nodes] of byBrand) {
    // Nearest-first within the brand; nodes without a distance sort last.
    nodes.sort((a, b) => {
      if (a.distanceKm != null && b.distanceKm != null) return a.distanceKm - b.distanceKm;
      if (a.distanceKm != null) return -1;
      if (b.distanceKm != null) return 1;
      return a.node.name.localeCompare(b.node.name);
    });
    brands.push({
      stationId,
      price: nodes[0].price!,
      nearestDistanceKm: nodes[0].distanceKm,
      nodes,
    });
  }

  brands.sort((a, b) => {
    if (a.price.voucherPerLiter !== b.price.voucherPerLiter) {
      return a.price.voucherPerLiter - b.price.voucherPerLiter;
    }
    if (a.nearestDistanceKm != null && b.nearestDistanceKm != null && a.nearestDistanceKm !== b.nearestDistanceKm) {
      return a.nearestDistanceKm - b.nearestDistanceKm;
    }
    if (a.nearestDistanceKm != null && b.nearestDistanceKm == null) return -1;
    if (a.nearestDistanceKm == null && b.nearestDistanceKm != null) return 1;
    return a.stationId.localeCompare(b.stationId);
  });

  return brands;
}

// JS \b word boundaries don't fire on Cyrillic, so anchor on segment edges/space instead.
const ADDRESS_ADMIN_SEGMENT = /облас|обл\.|(?:^|\s)район(?=\s|$)|(?:^|\s)р-н(?=\s|$)|^україна$/i;
// Settlement marker followed by a dot (optional space) OR whitespace — covers « м.Ізмаїл » (no space) and « смт Козова ».
const SETTLEMENT_PREFIX = /^(?:м|с|смт|с-ще|сел(?:о|ище)|пос)(?:\.\s*|\s+)/i;
/** OKKO tacks an internal site code onto the street — « вул. Зоряна, 2-А АЗК №01 ». */
const OKKO_SITE_CODE = /\s*АЗК\s*№?\s*\d+\s*$/i;
const HOUSE_PREFIX = /^буд\.?\s*/i;

/**
 * A short, human address for a station node: `{city}, {street}[, {number}]`.
 *
 * Node addresses arrive in two very different brand shapes. OKKO is already terse —
 * `вул. Хриплинська, 9 АЗК №01` — but carries an internal « АЗК №NN » site code. WOG is
 * verbose and administrative — `Одеська область, Ізмаїльський район, м.Ізмаїл,
 * пр.Незалежності, 378` — leading with oblast, raion and the settlement (which we already
 * hold in `node.city`). Both reduce to the same shape: take the city from the node (falling
 * back to the settlement segment), drop the administrative/settlement segments, keep the
 * street-and-number remainder, and strip the OKKO site code, a `буд.` prefix and `б/н`.
 *
 * Returns '' when nothing usable remains, so the caller can fall back to its own copy.
 */
export function formatShortAddress(node: Pick<StationNode, 'address' | 'city'>): string {
  const raw = (node.address ?? '').trim();
  const city = (node.city ?? '').trim();

  if (!raw) return city;

  const segments = raw
    .split(',')
    .map((s) => s.replace(/\s+/g, ' ').trim())
    .filter(Boolean);

  const kept: string[] = [];
  let derivedCity = '';
  for (const seg of segments) {
    if (ADDRESS_ADMIN_SEGMENT.test(seg)) continue; // oblast / raion / country
    if (SETTLEMENT_PREFIX.test(seg)) {
      // "м. Буча" / "село Фонтанка" — the settlement, i.e. the city. Remember it in case
      // the node has no city of its own, then drop it from the street remainder.
      if (!derivedCity) derivedCity = seg.replace(SETTLEMENT_PREFIX, '').trim();
      continue;
    }
    kept.push(seg);
  }

  const resolvedCity = city || derivedCity;

  // Drop a bare segment that merely repeats the city (no street marker), and clean house codes.
  const streetParts = kept
    .filter((seg) => !resolvedCity || seg.toLowerCase() !== resolvedCity.toLowerCase())
    .map((seg) => seg.replace(OKKO_SITE_CODE, '').replace(HOUSE_PREFIX, '').trim())
    .filter((seg) => seg && !/^б\/н$/i.test(seg));

  const parts = resolvedCity ? [resolvedCity, ...streetParts] : streetParts;
  return parts.join(', ');
}
