/**
 * The operator's "Фінал" preview: the price per litre the customer would pay for this fuel.
 *
 * It mirrors the server's `FuelPricing.FinalPerLiter` exactly - our price is the cost plus the
 * margin - so what an operator sees while typing is what the server charges. The column price is
 * the reference the customer's saving is measured against, NOT a term of the price: our price can
 * sit above it, and the customer sees that.
 */
export function computeFinal(
  cost: number | null,
  profit: number | null,
  pump: number | null,
): number {
  // No cost means no price of ours: the column is the only figure we honestly have, and the
  // storefront shows that with "not in stock" next to it. Before this, a blank cost was a
  // ZERO here and in the payload, which priced the fuel at its margin.
  if (cost === null || profit === null) return pump ?? 0;
  return cost + profit;
}
