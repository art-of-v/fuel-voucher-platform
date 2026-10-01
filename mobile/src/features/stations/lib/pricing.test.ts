import { fuelSaving } from './pricing';

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
});
