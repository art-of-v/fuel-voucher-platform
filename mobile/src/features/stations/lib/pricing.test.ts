import { canSell, fuelHeadlinePrice, fuelSaving } from './pricing';

describe('fuelSaving', () => {
  it('reports a real discount when basePrice is above discountPrice', () => {
    const r = fuelSaving({ basePrice: 102.9, discountPrice: 97.86, isAvailable: true });
    expect(r.hasSaving).toBe(true);
    expect(r.amount).toBeCloseTo(5.04, 2);
  });

  it('hides the saving when base equals discount (no pump discount, margin=0)', () => {
    // Regression for #112: a fuel priced at its headline must not show a struck
    // price nor a "0 ₴/L" pill.
    const r = fuelSaving({ basePrice: 97.86, discountPrice: 97.86, isAvailable: true });
    expect(r.hasSaving).toBe(false);
    expect(r.amount).toBe(0);
  });

  it('never returns a negative saving when base is below discount', () => {
    const r = fuelSaving({ basePrice: 95, discountPrice: 100, isAvailable: true });
    expect(r.hasSaving).toBe(false);
    expect(r.amount).toBe(0);
  });

  it('treats missing prices as zero', () => {
    const r = fuelSaving({ basePrice: 0, discountPrice: 0, isAvailable: true });
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
    const r = fuelSaving({ basePrice: 92.9, discountPrice: 0, isAvailable: true });
    expect(r.hasSaving).toBe(false);
    expect(r.amount).toBe(0);
  });

  it('shows no saving on a priced fuel that is out of stock', () => {
    // A fuel we cannot sell is shown at the COLUMN price, so the column price IS the price on
    // that shelf: the struck price would be the price crossed through against itself, and the
    // badge would measure a discount against a price the customer is not paying. The shelf
    // shows the column price and no saving, until the stock is there (planning #552).
    const r = fuelSaving({ basePrice: 102.9, discountPrice: 97.86, isAvailable: false });
    expect(r.hasSaving).toBe(false);
    expect(r.amount).toBe(0);
  });

  it('treats a payload without the availability flag as out of stock', () => {
    // The flag is optional on the wire. A payload that does not carry it must not promise a
    // saving: the price on that shelf is the column price, so there is nothing to save against.
    const r = fuelSaving({ basePrice: 102.9, discountPrice: 97.86 });
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

  it('shows the column price for a payload that does not say whether the fuel is in stock', () => {
    // The flag is optional on the wire, and an answer we do not have is not an answer of "in
    // stock". The shelf falls back to the column price - the same shelf the saving is hidden on.
    const price = fuelHeadlinePrice({
      discountPrice: 97.86,
      pumpPricePerLiter: 102.9,
      finalPricePerLiter: 97.86,
    });
    expect(price).toBeCloseTo(102.9, 2);
  });
});

describe('the price and the saving answer the same question', () => {
  it('hides the saving exactly where the shelf shows the column price', () => {
    // The pricing model is one rule: out of stock the shelf shows the column price, with no
    // crossed-out price and no saving, because the column price IS the price there. The two
    // helpers read the stock flag separately, so they are pinned together here.
    const outOfStock = {
      discountPrice: 97.86,
      pumpPricePerLiter: 102.9,
      finalPricePerLiter: 97.86,
      isAvailable: false,
    };

    expect(fuelHeadlinePrice(outOfStock)).toBeCloseTo(102.9, 2);
    expect(fuelSaving({ basePrice: 102.9, discountPrice: 97.86, isAvailable: false })).toEqual({
      amount: 0,
      hasSaving: false,
    });
  });

  it('counts only an explicit true as sellable', () => {
    // The card dims itself and prints the out-of-stock label on this predicate, so a payload that
    // does not carry the flag - a cached or an older one - must not light the row up as if the
    // fuel were on the shelf.
    expect(canSell({ isAvailable: true })).toBe(true);
    expect(canSell({ isAvailable: false })).toBe(false);
    expect(canSell({})).toBe(false);
  });
});
