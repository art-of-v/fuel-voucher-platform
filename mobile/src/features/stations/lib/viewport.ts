import type { StationNode } from '../../../core/types/api';

/** The shape `MapView`'s `onRegionChangeComplete` hands us. */
export type MapRegion = {
  latitude: number;
  longitude: number;
  latitudeDelta: number;
  longitudeDelta: number;
};

/** The pair of corners `MapView.getMapBoundaries()` resolves with. */
export type MapCorners = {
  northEast: { latitude: number; longitude: number };
  southWest: { latitude: number; longitude: number };
};

/**
 * The region the map is actually showing, from the corners it reports.
 *
 * MapKit never announces the camera it opens on: `initialRegion` is applied before the map has
 * drawn, the events it fires while it is still setting itself up are dropped by the native module
 * (`hasStartedRendering`), and the first region a pan reports is the first region JS hears about at
 * all. So a screen that picks its pins for a *guessed* region can hold pins that are nowhere near
 * the camera — an empty map that fills in on the first gesture, which reads as pins that never
 * loaded. Asking the map what it shows is the only answer that is true by construction.
 *
 * `null` for a map that cannot describe itself yet: a zero-sized view reports a nonsense box, and
 * every caller's fallback is better than mounting pins for it.
 */
export function regionFromBoundaries(bounds: MapCorners): MapRegion | null {
  const latitudeDelta = bounds.northEast.latitude - bounds.southWest.latitude;
  const longitudeDelta = bounds.northEast.longitude - bounds.southWest.longitude;
  if (!(latitudeDelta > 0) || !(longitudeDelta > 0)) return null;
  if (!Number.isFinite(latitudeDelta) || !Number.isFinite(longitudeDelta)) return null;
  return {
    latitude: (bounds.northEast.latitude + bounds.southWest.latitude) / 2,
    longitude: (bounds.northEast.longitude + bounds.southWest.longitude) / 2,
    latitudeDelta,
    longitudeDelta,
  };
}

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
 * The mounted set is expensive to replace: each station that enters the shortlist mounts a pin
 * subtree into its slot and each one that leaves empties one, both inside the frame right after the
 * gesture settles. So the screen keeps the box it computed for and only recomputes when this says
 * `false` — a small pan changes nothing at all, which is the point. (The markers themselves are not
 * re-created either; see {@link assignToSlots}.)
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

/**
 * How many markers the map mounts. It mounts this many, once, and never another.
 *
 * The same number as {@link MAX_MOUNTED_MARKERS} on purpose — a screen that mounted more pins than
 * it has slots would have nowhere to put them — but a different thing: the cap bounds what a region
 * asks for, this is what the screen builds. See {@link assignToSlots} for why the count has to be
 * fixed rather than merely small.
 */
export const MARKER_POOL_SIZE = MAX_MOUNTED_MARKERS;

/**
 * Which station each of the `size` mounted markers shows: `(T | null)[]`, always exactly `size` long.
 *
 * `Marker` is a *legacy* view manager hosted by a Fabric interop component, and the interop layer
 * plays the child list back to that manager as `insertReactSubview:atIndex:` — with the index the
 * shadow tree says, into an array the manager keeps itself (`AIRMap`'s `_reactSubviews`, whose
 * `addAnnotation:` calls also feed MapKit). Adding and removing in one transaction is how the two
 * get out of step, and either way it goes fatally, in Objective-C, with no JS error to read:
 * `NSInvalidArgumentException: object cannot be nil` when a child's view is not there yet (upstream
 * #5217 — and the same line, one branch over), and `NSRangeException: index N beyond bounds` when
 * the array is shorter than the index. Panning the network map did that on every gesture that
 * changed the set, so no amount of making the set *smaller* ever stopped it.
 *
 * So the set is fixed: the screen mounts `size` markers in one transaction once the map has rendered,
 * already carrying the pins of the region on screen, and after that a pan only ever changes which
 * station a marker shows. That is a prop update — `coordinate`, `opacity` — and MapKit moves an
 * annotation whose coordinate changes (upstream #5193 measures it as a lag, not a failure). Nothing
 * is inserted, nothing is removed, and the array and the shadow tree never disagree.
 *
 * What puts a brand badge on the pin is not this schedule, though. `AIRMapMarker` picks between its own
 * view and Apple's once, from `reactSubviews.count` (`shouldUsePinView`), and under Fabric a marker is
 * *always* childless at that moment: props are applied and the marker's own `finalizeUpdates:` run
 * before its parent mounts it, and the marker's child is a later mutation of the same transaction
 * (`RCTMountingManager.mm:82-89`). So the choice is settled at prop time instead, by the `image` every
 * `StationMarker` carries — see that file for why nothing else reads it.
 *
 * Two rules keep the pins steady inside that fixed set:
 *
 * - a station that already has a marker keeps it, so a pan does not move a pin the user can see to a
 *   different slot (and swap its press handler with it);
 * - the pins still to place go into the slots that are empty in the result. There are always enough:
 *   the shortlist is capped at `size`, so the stations that just left the region free at least as
 *   many slots as there are new ones to fill.
 */
export function assignToSlots<T extends Coords & { id: string }>(
  points: readonly T[],
  region: MapRegion,
  previous: readonly (T | null)[],
  size = MARKER_POOL_SIZE,
): (T | null)[] {
  // Keyed by id, so a slot is re-pointed at the *current* instance of the station rather than the
  // object it happened to hold when it was assigned. `markersForRegion` keeps the input order, so
  // this map's order is the order the thinning grid picked them in.
  const wanted = new Map(markersForRegion(points, region, size).map((point) => [point.id, point]));

  const slots: (T | null)[] = new Array<T | null>(size).fill(null);

  previous.slice(0, size).forEach((point, index) => {
    const held = point ? wanted.get(point.id) : undefined;
    if (!held) return;
    slots[index] = held;
    wanted.delete(held.id);
  });

  const fresh = [...wanted.values()];
  let next = 0;
  for (let index = 0; index < size && next < fresh.length; index++) {
    if (slots[index] == null) slots[index] = fresh[next++];
  }

  return slots;
}

/** Narrowing helper for the union the map screen actually holds. */
export type MapPoint = StationNode | (StationNode & { brandId?: string });
