import React from 'react';
import { render, screen } from '@testing-library/react-native';

import { FuelCard } from './FuelCard';
import type { FuelType, Station } from '../../../core/types/api';

/**
 * A row on the shelf says two things: what the fuel costs the customer, and what they save on
 * it. The second one is only true while we HAVE the fuel. Out of stock the shelf shows the column
 * price — so there is nothing crossed out and nothing to save, and a badge there would measure the
 * saving against a price the customer is not paying (planning #552).
 */

const station: Station = {
  id: 's1',
  slug: 'okko',
  name: 'OKKO',
  color: '#00E85F',
  logoText: 'OK',
  sortOrder: 1,
};

function mkFuel(overrides: Partial<FuelType> = {}): FuelType {
  return {
    id: 'ft1',
    name: 'А95',
    brandId: 'b1',
    brandSlug: 'okko',
    // The column price — what the pump charges — and the price we charge: the cost plus the margin.
    basePrice: 102.9,
    discountPrice: 97.86,
    pumpPricePerLiter: 102.9,
    finalPricePerLiter: 97.86,
    isAvailable: true,
    ...overrides,
  };
}

const renderCard = (overrides: Partial<FuelType> = {}) =>
  render(<FuelCard fuel={mkFuel(overrides)} station={station} index={0} onPress={jest.fn()} />);

describe('FuelCard saving (#552)', () => {
  it('shows the struck column price and the saving for a fuel in stock', () => {
    renderCard();

    // The column price crossed out, and the saving it makes: 102,90 − 97,86.
    expect(screen.getByText('102,90')).toBeTruthy();
    expect(screen.getByText('5,04')).toBeTruthy();
  });

  it('shows neither on a priced fuel that is out of stock', () => {
    // The shelf shows the column price, so the column price struck through against itself, and a
    // badge for a discount nobody would get, are both things the customer reads as true.
    renderCard({ isAvailable: false });

    expect(screen.queryByText('102,90')).toBeNull();
    expect(screen.queryByText('5,04')).toBeNull();
  });

  it('still shows the price on an out-of-stock fuel', () => {
    // Hiding the saving is not hiding the fuel: the column price is the only honest number left,
    // and the customer still needs to see what the market charges.
    renderCard({ isAvailable: false });

    expect(screen.getAllByText(/102,90/).length).toBeGreaterThan(0);
  });
});
