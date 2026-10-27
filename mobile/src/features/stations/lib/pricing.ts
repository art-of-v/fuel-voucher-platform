import type { FuelType } from '../../../core/types/api';

export interface FuelSaving {
  /** Per-litre saving in UAH. Always ≥ 0 — zero when there is no real discount. */
  amount: number;
  /**
   * True only when the fuel is IN STOCK and the list/pump "before" price
   * (`basePrice`, planning #73) sits above the price we sell it for. The catalog
   * must then show the struck price and the savings pill; otherwise it must hide
   * both, so we never render a meaningless "0 ₴/L" badge, a negative saving for a
   * fuel priced at its headline, or a saving on a shelf the customer cannot buy
   * from (planning #552).
   */
  hasSaving: boolean;
}

/**
 * Whether the shop can sell this fuel right now.
 *
 * <p>A payload that does not carry the flag counts as "no". The shelf shows the COLUMN price in
 * that case, and a saving measured against a price the customer is not paying is exactly the lie
 * the pricing model forbids. The price and the saving answer the same question, so they ask it in
 * the same place — one predicate, so they cannot drift apart.
 *
 * <p>The row's COLOUR asks the same question too: an empty shelf is greyed out and says so, a
 * sellable one keeps the brand accent. That is why this is exported rather than private — a card
 * that dims itself on a different rule than the price it prints would show a pale row at a price
 * we do not charge.
 */
export function canSell(fuel: Pick<FuelType, 'isAvailable'>): boolean {
  return fuel.isAvailable === true;
}

/**
 * Decide whether a fuel row has a real per-litre discount, and by how much.
 * Mirrors the package-level rule in `PackageCard` (originalPrice > price).
 */
export function fuelSaving(
  fuel: Pick<FuelType, 'basePrice' | 'discountPrice' | 'isAvailable'>,
): FuelSaving {
  const base = fuel.basePrice || 0;
  const discount = fuel.discountPrice || 0;
  // A discount price of ZERO is not a discount, it is a price: it says the customer takes the
  // fuel for nothing. The guard is `base > discount`, which a zero passes, so a fuel whose
  // stored discount price is zero used to be shown with the whole column price struck through
  // and a badge promising a saving of the whole column price - fourteen of the twenty-three
  // fuels on the catalogue, while the price they are sold at is the column price itself.
  //
  // A saving needs a real price on both sides. The zero here is "not set", and "not set" is
  // shown as no saving at all.
  //
  // And a saving needs the fuel to be IN STOCK. A fuel we cannot sell is shown at the COLUMN
  // price, so the column price IS the price on that shelf: there is nothing to cross out and
  // nothing to save, and a badge would measure the saving against a price the customer is not
  // paying. A priced fuel whose stock ran out used to show the column price struck through
  // against itself, with a badge for a discount nobody would get (planning #552).
  const hasSaving = canSell(fuel) && discount > 0 && base > discount;
  return { amount: hasSaving ? base - discount : 0, hasSaving };
}

/**
 * The price to show for a fuel, per litre.
 *
 * <p>WHICH price depends on whether the shop can sell it:
 *
 * <ul>
 *   <li><b>available</b> — our own price: the cost plus the margin. The column price is the
 *       reference it is shown against, not a term of it, so our price can sit above it. That is
 *       what the customer pays, so it is what the row has to say.</li>
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
  const ours = fuel.finalPricePerLiter ?? undefined;
  const column = fuel.pumpPricePerLiter ?? undefined;
  const legacy = fuel.discountPrice || undefined;

  const price = canSell(fuel) ? (ours ?? column ?? legacy) : (column ?? ours ?? legacy);

  // Zero is not a price. It is what an unpriced fuel carries in the column, and rendering it would
  // put "0 ₴" on the shelf.
  return price !== undefined && price > 0 ? price : null;
}
