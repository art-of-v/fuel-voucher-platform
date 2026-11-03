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
 * Whether the region on screen is still served by the box the markers were last computed for.
 *
 * The mounted set is expensive to replace: every marker that enters or leaves it is a native view
 * tree created inside the frame right after the gesture settles, and a pin created while `MapView`
 * is still resolving its own mount is what the iOS interop layer trips over. So the screen keeps
 * the box it computed for and only recomputes when this says `false` — a small pan changes nothing
 * at all, which is the point.
 *
 * Two ways to outgrow a box, and both are checked against {@link VIEWPORT_PADDING} (the margin the
 * set really covers, since `markersForRegion` pads the box it is given):
 *
 * - **pan out of it** — the visible span slides past the padded edge of the box;
 * - **zoom out of it** — the box becomes so much wider than the screen that it is thinning pins for
 *   an area nobody is looking at, and the street the user zoomed into shows three pins.
 *
 * A region identical to the box is covered: offset 0, and `delta <= delta * 1.2`.
 */
export function isRegionCoveredByBox(viewport: MapRegion, box: MapRegion): boolean {
  const covered = (vpCentre: number, vpDelta: number, boxCentre: number, boxDelta: number) =>
    Math.abs(vpCentre - boxCentre) * 2 + vpDelta <= boxDelta * (1 + VIEWPORT_PADDING) &&
    boxDelta <= vpDelta * (1 + VIEWPORT_PADDING);

  return (
    covered(viewport.latitude, viewport.latitudeDelta, box.latitude, box.latitudeDelta) &&
    covered(viewport.longitude, viewport.longitudeDelta, box.longitude, box.longitudeDelta)
  );
}

/**
 * The stations worth mounting for the region currently on screen.
 *
 * The map fetches every station node once and re-renders on each pan, so this is the only thing
 * standing between a pan that mounts nothing and a pan that mounts 1036 native view trees at
 * once. Each pin is a live tree (`react-native-maps` does not recycle MapKit annotation views —
 * the `reuseIdentifier` property is commented out in its own marker manager), and each one that
 * appears in a transaction is a child the Fabric interop layer has to mount, so the count is the
 * exposure. `markersForRegion` bounds it.
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
 * Above it the count grows linearly with the area the screen asks for, and every extra pin is
 * another native tree created in that request's transaction — which is why the screen only asks
 * when {@link isRegionCoveredByBox} says the box it has no longer covers what is on screen.
 */
export const MAX_MOUNTED_MARKERS = 120;

/**
 * The pins to mount for a region: every station inside it, thinned to `limit` at most.
 *
 * Thinning is a grid, not a prefix. `inView.slice(0, limit)` would keep whichever АЗК the API
 * happened to list first — all of one brand, all of one city — so a zoomed-out map would show
 * pins clustered in one corner and nothing where the user is looking. Splitting the area into
 * cells and keeping the АЗК nearest each cell's centre keeps the spread and leaves the dense areas
 * to the ranking sheet, which is the part of the screen that is not limited.
 *
 * The grid is anchored to absolute coordinates, not to the region, and that is the reason this
 * function can be called again after a pan. `floor(lat / cellLat)` puts the same line through the
 * same place on the ground whoever asks, so the АЗК that won a cell keeps winning it and the pins
 * already mounted stay mounted. Anchored to the region instead, moving the centre moves every cell
 * boundary, every cell changes its winner, and a pan that revealed nothing new still re-creates all
 * `limit` native trees — in the frame right after the gesture settles, which is the exposure the
 * interop crash needs. A zoom changes `cellLat`, so it does re-shuffle: that is a different scale,
 * and {@link isRegionCoveredByBox} asks for a recomputation at it anyway.
 *
 * `cells²` bounds the picks only while the grid and the area start on the same line. On an absolute
 * grid the box can straddle `cells + 1` rows, so the search steps down instead of trusting it, and
 * one cell always fits inside the budget.
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

  for (let cells = Math.ceil(Math.sqrt(limit)); cells > 1; cells--) {
    const picked = onePerCell(inView, latSpan / cells, lngSpan / cells);
    if (picked.length <= limit) return picked;
  }

  return onePerCell(inView, latSpan, lngSpan);
}

/** The station nearest each cell's centre, emitted in the order the stations arrived. */
function onePerCell<T extends Coords>(points: readonly T[], cellLat: number, cellLng: number): T[] {
  const best = new Map<string, { point: T; away: number }>();
  for (const point of points) {
    // The caller has already run these through `pointsInRegion`, which drops any point without
    // finite coordinates; the grid would put a NaN in cell `0:0` and steal that corner.
    const lat = point.lat as number;
    const lng = point.lng as number;
    const row = Math.floor(lat / cellLat);
    const col = Math.floor(lng / cellLng);
    const key = `${row}:${col}`;
    const away = (lat - (row + 0.5) * cellLat) ** 2 + (lng - (col + 0.5) * cellLng) ** 2;
    const held = best.get(key);
    if (!held || away < held.away) best.set(key, { point, away });
  }

  const chosen = new Set<T>([...best.values()].map((held) => held.point));
  return points.filter((point) => chosen.has(point));
}

/** Narrowing helper for the union the map screen actually holds. */
export type MapPoint = StationNode | (StationNode & { brandId?: string });
