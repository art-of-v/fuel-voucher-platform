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

/**
 * The price to show for a fuel, per litre.
 *
 * <p>The SERVER decides this and sends it in `discountPrice` - so this reads that number, and the
 * fields below are the ones it was decided from, kept for the reference price and for the reason.
 *
 * <p>WHICH price depends on whether the shop can sell it:
 *
 * <ul>
 *   <li><b>available</b> — our own price: the cost plus the margin, capped at the pump. That is what
 *       the customer pays, so it is what the row has to say.</li>
 *   <li><b>not available</b> — the column price: what the network charges. We have no price of our
 *       own to quote (an imported fuel has no margin until an operator sets one), and showing a zero
 *       or a market price presented as ours is a lie the customer pays for.</li>
 * </ul>
 *
 * <p>Null means there is no number to show at all, and the caller must NOT render a price in that
 * case. A row that says "0 ₴" is a free fuel; a row that says nothing is a fuel we have not priced.
 */
export function fuelHeadlinePrice(
  fuel: Pick<
    FuelType,
    'discountPrice' | 'pumpPricePerLiter' | 'finalPricePerLiter' | 'isAvailable'
  >,
): number | null {
  const shown = fuel.discountPrice || undefined;

  // Zero is not a price. It is what an unpriced fuel carries in the column, and rendering it would
  // put "0 ₴" on the shelf. The fields the server decided from are the fallback, for a payload that
  // predates the rule.
  if (shown !== undefined && shown > 0) {
    return shown;
  }

  const ours = fuel.finalPricePerLiter ?? undefined;
  const column = fuel.pumpPricePerLiter ?? undefined;

  const price = fuel.isAvailable ? (ours ?? column) : (column ?? ours);

  return price !== undefined && price > 0 ? price : null;
}
