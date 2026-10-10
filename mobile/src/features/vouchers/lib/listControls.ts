import type { Voucher } from '../../../core/types/api';

/**
 * Sort and filter primitives for a flat voucher list.
 *
 * These exist for the worker's own list (#161 H0): a worker holds one to three vouchers, so the list
 * stays flat rather than becoming a hub - but once it grows, "which of mine is about to expire" needs
 * an answer that is not "scroll and read every date". The owner-side lists reuse the same functions,
 * so the behaviour cannot drift between the two.
 *
 * Pure functions with no React: the sorting is the part that is easy to get subtly wrong, and it has
 * to be testable without a renderer.
 */

export type VoucherSortKey = 'expiry' | 'provider' | 'fuel' | 'liters';
export type SortDirection = 'asc' | 'desc';

export interface VoucherFilters {
  /** Exact provider match (the brand, e.g. OKKO). */
  provider?: string;
  /** Exact fuel-type match. */
  fuel?: string;
  /** Exact status match, as the API reports it. */
  status?: string;
  /**
   * Hide vouchers whose expiration date is in the past. Defaults to false - hiding by default would
   * silently remove a lapsed voucher the worker still needs to see.
   */
  hideExpired: boolean;
}

export const EMPTY_FILTERS: VoucherFilters = { hideExpired: false };

export interface ListControls {
  sortKey: VoucherSortKey;
  sortDirection: SortDirection;
  filters: VoucherFilters;
}

function isExpired(voucher: Voucher, now: Date): boolean {
  if (!voucher.expirationDate) return false;
  const expiry = new Date(voucher.expirationDate);
  if (Number.isNaN(expiry.getTime())) return false;
  // Compare day to day, not instants: `expirationDate` is a date-only value that parses as UTC
  // midnight, so comparing it against "now" would mark a voucher expiring *today* as expired for
  // the whole day - which is exactly when a worker most needs to see it.
  const expiryDay = Date.UTC(expiry.getUTCFullYear(), expiry.getUTCMonth(), expiry.getUTCDate());
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  return expiryDay < today;
}

/**
 * The distinct providers and fuels present in the list, for populating the filter options.
 *
 * Derived from the data rather than a fixed catalogue: the platform supports any number of brands, and
 * a dropdown offering a filter that matches nothing is worse than no dropdown.
 */
export function availableFacets(vouchers: Voucher[]): { providers: string[]; fuels: string[] } {
  const providers = new Set<string>();
  const fuels = new Set<string>();
  for (const v of vouchers) {
    if (v.provider) providers.add(v.provider);
    if (v.fuelType) fuels.add(v.fuelType);
  }
  const byName = (a: string, b: string) => a.localeCompare(b);
  return {
    providers: [...providers].sort(byName),
    fuels: [...fuels].sort(byName),
  };
}

/** How many filters are currently narrowing the list - drives the "clear (N)" affordance. */
export function countActiveFilters(filters: VoucherFilters): number {
  let count = 0;
  if (filters.provider) count++;
  if (filters.fuel) count++;
  if (filters.status) count++;
  if (filters.hideExpired) count++;
  return count;
}

function matchesFilters(voucher: Voucher, filters: VoucherFilters, today: Date): boolean {
  if (filters.provider && voucher.provider !== filters.provider) return false;
  if (filters.fuel && voucher.fuelType !== filters.fuel) return false;
  if (filters.status && voucher.status !== filters.status) return false;
  if (filters.hideExpired && isExpired(voucher, today)) return false;
  return true;
}

function compareVouchers(a: Voucher, b: Voucher, key: VoucherSortKey): number {
  switch (key) {
    case 'expiry': {
      // A voucher with no date sorts last rather than first: an unknown expiry is not the most urgent.
      if (!a.expirationDate && !b.expirationDate) return 0;
      if (!a.expirationDate) return 1;
      if (!b.expirationDate) return -1;
      return new Date(a.expirationDate).getTime() - new Date(b.expirationDate).getTime();
    }
    case 'provider':
      return (a.provider ?? '').localeCompare(b.provider ?? '');
    case 'fuel':
      return (a.fuelName || a.fuelType || '').localeCompare(b.fuelName || b.fuelType || '');
    case 'liters':
      return (a.amount ?? 0) - (b.amount ?? 0);
  }
}

/**
 * Apply filters, then sort. Returns a new array - the caller's list is never mutated, because the
 * wallet holds the same voucher objects in the orders it builds elsewhere.
 */
export function applyListControls(
  vouchers: Voucher[],
  controls: ListControls,
  now: Date = new Date(),
): Voucher[] {
  const filtered = vouchers.filter((v) => matchesFilters(v, controls.filters, now));

  const sorted = [...filtered].sort((a, b) => {
    const result = compareVouchers(a, b, controls.sortKey);
    // Ties fall back to expiry so a provider- or fuel-sorted list still reads chronologically.
    const tieBreak = result === 0 ? compareVouchers(a, b, 'expiry') : result;
    return controls.sortDirection === 'asc' ? tieBreak : -tieBreak;
  });

  return sorted;
}

/**
 * Toggle a sort key: tapping the active key flips direction, tapping a new one starts ascending
 * except for litres, where the useful first view is the biggest tank rather than the smallest.
 */
export function toggleSort(controls: ListControls, key: VoucherSortKey): ListControls {
  if (controls.sortKey !== key) {
    return { ...controls, sortKey: key, sortDirection: key === 'liters' ? 'desc' : 'asc' };
  }
  return {
    ...controls,
    sortDirection: controls.sortDirection === 'asc' ? 'desc' : 'asc',
  };
}
