import type { FuelType } from '../../../core/types/api';

export interface FuelSaving {
  /** Per-litre saving in UAH. Always ≥ 0 — zero when there is no real discount. */
  amount: number;
  /**
   * True only when the list/pump "before" price (`basePrice`, planning #73) sits
   * above the sale price. The catalog must then show the struck price and the
   * savings pill; otherwise it must hide both, so we never render a meaningless
   * "0 ₴/L" badge or a negative saving for a fuel priced at its headline.
   */
  hasSaving: boolean;
}

/**
 * Decide whether a fuel row has a real per-litre discount, and by how much.
 * Mirrors the package-level rule in `PackageCard` (originalPrice > price).
 */
export function fuelSaving(fuel: Pick<FuelType, 'basePrice' | 'discountPrice'>): FuelSaving {
  const base = fuel.basePrice || 0;
  const discount = fuel.discountPrice || 0;
  const hasSaving = base > discount;
  return { amount: hasSaving ? base - discount : 0, hasSaving };
}
