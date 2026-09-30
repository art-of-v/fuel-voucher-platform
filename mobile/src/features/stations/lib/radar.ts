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
