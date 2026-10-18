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

type Coords = { lat?: string | null; lng?: string | null };

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
  const lat = parseFloat(point.lat ?? '');
  const lng = parseFloat(point.lng ?? '');

  if (!Number.isFinite(lat) || !Number.isFinite(lng)) return false;

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
 */
export function pointsInRegion<T extends Coords>(points: readonly T[], region: MapRegion): T[] {
  return points.filter((point) => isPointInRegion(point, region));
}

/** Narrowing helper for the union the map screen actually holds. */
export type MapPoint = StationNode | (StationNode & { brandId?: string });
