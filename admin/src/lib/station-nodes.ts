// Pure, framework-free helpers for the "Точки АЗК" (station nodes) admin page.
// Kept out of the component so they can be unit-tested without rendering
// (this worktree can't run the admin dev deps — see CI for the type/build gate).

/** Mirrors the server's BrandStation entity, which the admin endpoint returns as-is. */
export interface StationNode {
  /** Surrogate key. Server-assigned; the row cannot be renamed through the API. */
  id: string;
  /**
   * The importer's crosswalk key, unique per brand. It was the primary key until the
   * uuid migration, which is why re-importing a network's file still heals its rows.
   * The server derives it from the coordinates when a caller leaves it blank.
   */
  externalId: string;
  /** The owning network's uuid. */
  brandId: string;
  name: string;
  address?: string | null;
  phone?: string | null;
  city?: string | null;
  stationType?: string | null;
  lat?: number | null;
  lng?: number | null;
  createdAtUtc?: string;
  updatedAtUtc?: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

// WGS84 bounds — mirrors backend StationNodeCoordinates.IsValidLat / IsValidLng.
export const LAT_MIN = -90;
export const LAT_MAX = 90;
export const LNG_MIN = -180;
export const LNG_MAX = 180;

export function isValidLat(lat: number): boolean {
  return Number.isFinite(lat) && lat >= LAT_MIN && lat <= LAT_MAX;
}

export function isValidLng(lng: number): boolean {
  return Number.isFinite(lng) && lng >= LNG_MIN && lng <= LNG_MAX;
}

export function isValidCoordinate(lat: number, lng: number): boolean {
  return isValidLat(lat) && isValidLng(lng);
}

// Convenience-only mirror of backend BrandStationCoordinates.DeriveId
// ({brandSlug}-{lat:F5}-{lng:F5}). The server is authoritative and derives the same
// value when a create leaves the external id blank; this only pre-fills that field.
// It takes the brand's SLUG, not its uuid — seeding the uuid here would write a key
// no import file could ever produce.
export function deriveExternalId(
  brandSlug: string,
  lat: number,
  lng: number,
): string {
  return `${brandSlug}-${lat.toFixed(5)}-${lng.toFixed(5)}`;
}
