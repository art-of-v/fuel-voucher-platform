// Pure, framework-free helpers for the "Точки АЗК" (station nodes) admin page.
// Kept out of the component so they can be unit-tested without rendering
// (this worktree can't run the admin dev deps — see CI for the type/build gate).

export interface StationNode {
  id: string;
  stationId: string;
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

// Convenience-only mirror of backend StationNodeCoordinates.DeriveId
// ({stationId}-{lat:F5}-{lng:F5}). The server is authoritative on import and
// requires an explicit id on single create; this just pre-fills that field.
export function deriveNodeId(stationId: string, lat: number, lng: number): string {
  return `${stationId}-${lat.toFixed(5)}-${lng.toFixed(5)}`;
}
