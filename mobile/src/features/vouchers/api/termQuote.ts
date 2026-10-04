import { apiFetch } from '../../../core/api/apiClient';

/**
 * A validity term the customer can buy fuel for, with the price they would actually pay for the line.
 *
 * A shorter term earns a bigger discount, which is the whole incentive: the customer commits to less
 * up front and buys the difference back later if they cannot use the fuel in time.
 */
export interface TermQuoteItem {
  term: string;
  discountPerLiterUah: number;
  pricePerLiterUah: number | null;
  linePriceUah: number;
  liters: number;
  available: boolean;
}

export interface TermQuote {
  enabled: boolean;
  terms: TermQuoteItem[];
}

/**
 * Read-only preview of the term ladder for one cart line.
 *
 * Non-binding: nothing is reserved and no price is frozen. Checkout recomputes server-side, so a manager
 * editing the ladder mid-session can change what the picker shows but never what is charged.
 */
export async function getTermQuote(
  stationId: string,
  fuelTypeId: string,
  liters: number,
): Promise<TermQuote> {
  const query = new URLSearchParams({ stationId, fuelTypeId, liters: String(liters) });
  return apiFetch<TermQuote>(`/api/purchases/term-quote?${query.toString()}`);
}