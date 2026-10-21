import { fuelHeadlinePrice, fuelSaving } from './pricing';

describe('fuelSaving', () => {
  it('reports a real discount when basePrice is above discountPrice', () => {
    const r = fuelSaving({ basePrice: 102.9, discountPrice: 97.86 });
    expect(r.hasSaving).toBe(true);
    expect(r.amount).toBeCloseTo(5.04, 2);
  });

  it('hides the saving when base equals discount (no pump discount, margin=0)', () => {
    // Regression for #112: a fuel priced at its headline must not show a struck
    // price nor a "0 ₴/L" pill.
    const r = fuelSaving({ basePrice: 97.86, discountPrice: 97.86 });
    expect(r.hasSaving).toBe(false);
    expect(r.amount).toBe(0);
  });

  it('never returns a negative saving when base is below discount', () => {
    const r = fuelSaving({ basePrice: 95, discountPrice: 100 });
    expect(r.hasSaving).toBe(false);
    expect(r.amount).toBe(0);
  });

  it('treats missing prices as zero', () => {
    const r = fuelSaving({ basePrice: 0, discountPrice: 0 });
    expect(r.hasSaving).toBe(false);
    expect(r.amount).toBe(0);
  });

  it('shows no saving when the discount price is zero', () => {
    // A discount price of ZERO is a price, not a discount: it says the customer takes the fuel
    // for nothing. The fuel is sold at the column price, so a "saving" of the whole column
    // price - a struck-through price and a badge - is a lie the customer reads.
    //
    // This is the state of the catalogue: fourteen of the twenty-three fuels carried a stored
    // discount price of zero while their price is the column price.
    const r = fuelSaving({ basePrice: 92.9, discountPrice: 0 });
    expect(r.hasSaving).toBe(false);
    expect(r.amount).toBe(0);
  });
});

describe('fuelHeadlinePrice', () => {
  it('shows OUR price when the shop can sell the fuel', () => {
    const price = fuelHeadlinePrice({
      discountPrice: 97.86,
      pumpPricePerLiter: 102.9,
      finalPricePerLiter: 97.86,
      isAvailable: true,
    });
    expect(price).toBeCloseTo(97.86, 2);
  });

  it('shows the COLUMN price when the shop cannot sell the fuel', () => {
    // An imported fuel has no margin of ours yet, so the only honest number is what the network
    // charges. The operator has not priced it, and the row must not claim otherwise.
    const price = fuelHeadlinePrice({
      discountPrice: 0,
      pumpPricePerLiter: 92.9,
      finalPricePerLiter: null,
      isAvailable: false,
    });
    expect(price).toBeCloseTo(92.9, 2);
  });

  it('never shows zero for a fuel nobody has priced', () => {
    // "0 UAH" is a free fuel. No number at all is a fuel we have not priced.
    const price = fuelHeadlinePrice({
      discountPrice: 0,
      pumpPricePerLiter: 0,
      finalPricePerLiter: null,
      isAvailable: false,
    });
    expect(price).toBeNull();
  });

  it('falls back to the column price when we have no price of our own but the shop can sell it', () => {
    const price = fuelHeadlinePrice({
      discountPrice: 0,
      pumpPricePerLiter: 92.9,
      finalPricePerLiter: null,
      isAvailable: true,
    });
    expect(price).toBeCloseTo(92.9, 2);
  });

  it('shows our price even when it is below the column price', () => {
    const price = fuelHeadlinePrice({
      discountPrice: 97.86,
      pumpPricePerLiter: 102.9,
      finalPricePerLiter: 97.86,
      isAvailable: true,
    });
    expect(price).toBeCloseTo(97.86, 2);
  });
});
