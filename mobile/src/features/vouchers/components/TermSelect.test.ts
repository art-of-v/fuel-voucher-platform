import { linePriceForTerm } from './TermSelect';
import type { TermQuote } from '../api/termQuote';

const buildQuote = (overrides: Partial<TermQuote> = {}): TermQuote => ({
  enabled: true,
  terms: [
    {
      term: '1w',
      discountPerLiterUah: 5,
      pricePerLiterUah: 94.9,
      linePriceUah: 949,
      liters: 10,
      available: true,
    },
    {
      term: '1m',
      discountPerLiterUah: 3,
      pricePerLiterUah: 96.9,
      linePriceUah: 969,
      liters: 10,
      available: true,
    },
    {
      term: '4m',
      discountPerLiterUah: 0.5,
      pricePerLiterUah: 99.4,
      linePriceUah: 994,
      liters: 10,
      available: false,
    },
  ],
  ...overrides,
});

describe('linePriceForTerm', () => {
  const packagePrice = 999;

  it('falls back to the package price when no term is chosen', () => {
    // The full remaining term is what the customer bought before this feature existed, and it is the
    // price every discount is measured against.
    expect(linePriceForTerm(buildQuote(), undefined, packagePrice)).toBe(packagePrice);
  });

  it('falls back to the package price when there is no quote at all', () => {
    // A quote that failed to load must not change what is charged - and in that state the picker is not
    // rendered either, so the card looks exactly as it did before the feature shipped.
    expect(linePriceForTerm(null, '1w', packagePrice)).toBe(packagePrice);
  });

  it('uses the quoted line price for the chosen term', () => {
    expect(linePriceForTerm(buildQuote(), '1w', packagePrice)).toBe(949);
    expect(linePriceForTerm(buildQuote(), '1m', packagePrice)).toBe(969);
  });

  it('ignores a term the server marked unbuyable', () => {
    // The picker greys these out, but a cart persisted from before a manager switched the tier off could
    // still hold one. Falling back is the safe direction: the customer sees the normal price rather than a
    // discount checkout will refuse.
    expect(linePriceForTerm(buildQuote(), '4m', packagePrice)).toBe(packagePrice);
  });

  it('ignores a term that is not on the ladder', () => {
    expect(linePriceForTerm(buildQuote(), '7y', packagePrice)).toBe(packagePrice);
  });

  it('with no sellable term at all, falls back to the package price', () => {
    // #182: the server now drops every term no stock can honour. When the whole ladder is gated out the
    // card must show its normal shelf price - not a discount from a term that is not on offer.
    const gatedOut = buildQuote({
      terms: buildQuote().terms.map((term) => ({ ...term, available: false })),
    });
    expect(linePriceForTerm(gatedOut, '1w', packagePrice)).toBe(packagePrice);
  });

  it('would honour a quote that prices above the package', () => {
    // Documenting a real edge rather than asserting a guard that does not exist: a term priced ABOVE the
    // shelf would make "shorter term, cheaper" false and the saving line negative. The server cannot
    // produce it - the ladder is a discount off the shelf price - so this test exists to say that the
    // card's arithmetic trusts the quote rather than clamping it.
    const inflated = buildQuote({
      terms: [{ ...buildQuote().terms[0], linePriceUah: 1200 }],
    });
    expect(linePriceForTerm(inflated, '1w', packagePrice)).toBe(1200);
  });
});
