import type { StationNode } from '../../../core/types/api';

/** The shape `MapView`'s `onRegionChangeComplete` hands us. */
export type MapRegion = {
  latitude: number;
  longitude: number;
  latitudeDelta: number;
  longitudeDelta: number;
};

/**
 * How much wider than the visible region we keep markers for, as a fraction of each delta.
 *
 * Without a margin, a marker sitting just outside the edge pops in only once the user has
 * panned past it, which reads as the map "loading late". A fifth of the viewport on every side
 * is enough that markers arrive before they are needed without meaningfully growing the set —
 * the whole point is to keep the mounted-marker count in the tens, not the thousands.
 */
export const VIEWPORT_PADDING = 0.2;

type Coords = { lat?: number | null; lng?: number | null };

/**
 * A point is inside the padded region when it falls within the region's delta on both axes.
 *
 * Map regions are defined by a centre and a span, so the bounds are simply centre ± half the
 * span; the padding widens that box. Longitudes are compared the plain way rather than with a
 * wrap-around: a region that straddles the antimeridian has no stations in front of it in this
 * network, and treating it as empty is the safe reading.
 */
export function isPointInRegion(
  point: Coords,
  region: MapRegion,
  padding = VIEWPORT_PADDING,
): boolean {
  const lat = point.lat;
  const lng = point.lng;

  // A point with no coordinates is not in any region, and it must not be rendered:
  // NaN in a coordinate is how a marker ends up at the top of the world.
  if (lat == null || lng == null || !Number.isFinite(lat) || !Number.isFinite(lng)) return false;

  const latPad = (region.latitudeDelta / 2) * (1 + padding);
  const lngPad = (region.longitudeDelta / 2) * (1 + padding);

  return Math.abs(lat - region.latitude) <= latPad && Math.abs(lng - region.longitude) <= lngPad;
}

/**
 * The stations worth mounting for the region currently on screen.
 *
 * The map fetches every station node once and re-renders on each pan, so this is the only thing
 * standing between a 1300-station network and ~1300 native views at once — which the OS answers
 * by killing the process under memory pressure.
 *
 * Every pin is a live view tree (`react-native-maps` does not recycle MapKit annotation views —
 * the `reuseIdentifier` property is commented out in its own marker manager), and each tree
 * carries a shadowed pin and a shadowed callout card whether or not anyone opens it. So this is
 * the raw material of the mounted set, and `markersForRegion` is what bounds it.
 */
export function pointsInRegion<T extends Coords>(points: readonly T[], region: MapRegion): T[] {
  return points.filter((point) => isPointInRegion(point, region));
}

/**
 * The most pins mounted at once.
 *
 * 120 is not a taste number: it covers a whole city at the default zoom (the Kyiv region at
 * `latitudeDelta` 0.15 holds 72 of the network's 1322 АЗК, and every one of them stays a pin)
 * and only starts thinning once the map is pulled back far enough that the pins overlap anyway.
 * Above it, the native view count grows linearly with the area on screen — which is what a pan
 * changes — and the process dies with no JS error to read.
 */
export const MAX_MOUNTED_MARKERS = 120;

/**
 * The pins to mount for a region: every station inside it, thinned to `limit` at most.
 *
 * Thinning is a grid, not a prefix. `inView.slice(0, limit)` would keep whichever АЗК the API
 * happened to list first — all of one brand, all of one city — so a zoomed-out map would show
 * pins clustered in one corner and nothing where the user is looking. Splitting the padded box
 * into cells and keeping the АЗК nearest each cell's centre keeps the spread, keeps the result
 * identical for the same region (a pan never re-shuffles the pins it keeps), and leaves the
 * dense areas to the ranking sheet, which is the part of the screen that is not limited.
 */
export function markersForRegion<T extends Coords>(
  points: readonly T[],
  region: MapRegion,
  limit = MAX_MOUNTED_MARKERS,
): T[] {
  const inView = pointsInRegion(points, region);
  if (inView.length <= limit) return inView;

  const latSpan = region.latitudeDelta * (1 + VIEWPORT_PADDING);
  const lngSpan = region.longitudeDelta * (1 + VIEWPORT_PADDING);
  const minLat = region.latitude - latSpan / 2;
  const minLng = region.longitude - lngSpan / 2;

  // Coarse to fine: a cells×cells grid holds at most cells² picks, so the first count worth
  // trying is the square root of the budget. Stepping down by one is enough because each step
  // only ever removes picks, and one cell always fits inside the budget.
  for (let cells = Math.ceil(Math.sqrt(limit)); cells > 1; cells--) {
    const picked = onePerCell(inView, cells, minLat, minLng, latSpan, lngSpan);
    if (picked.length <= limit) return picked;
  }

  return onePerCell(inView, 1, minLat, minLng, latSpan, lngSpan);
}

/** The station nearest each cell's centre, emitted in the order the stations arrived. */
function onePerCell<T extends Coords>(
  points: readonly T[],
  cells: number,
  minLat: number,
  minLng: number,
  latSpan: number,
  lngSpan: number,
): T[] {
  const cellLat = latSpan / cells;
  const cellLng = lngSpan / cells;
  const rowOf = (lat: number) =>
    Math.min(cells - 1, Math.max(0, Math.floor((lat - minLat) / cellLat)));
  const colOf = (lng: number) =>
    Math.min(cells - 1, Math.max(0, Math.floor((lng - minLng) / cellLng)));

  const best = new Map<string, { point: T; away: number }>();
  for (const point of points) {
    // The caller has already run these through `pointsInRegion`, which drops any point without
    // finite coordinates; the grid would put a NaN in cell `0:0` and steal that corner.
    const lat = point.lat as number;
    const lng = point.lng as number;
    const row = rowOf(lat);
    const col = colOf(lng);
    const key = `${row}:${col}`;
    const centreLat = minLat + (row + 0.5) * cellLat;
    const centreLng = minLng + (col + 0.5) * cellLng;
    const away = (lat - centreLat) ** 2 + (lng - centreLng) ** 2;
    const held = best.get(key);
    if (!held || away < held.away) best.set(key, { point, away });
  }

  const chosen = new Set<T>([...best.values()].map((held) => held.point));
  return points.filter((point) => chosen.has(point));
}

/** Narrowing helper for the union the map screen actually holds. */
export type MapPoint = StationNode | (StationNode & { brandId?: string });
