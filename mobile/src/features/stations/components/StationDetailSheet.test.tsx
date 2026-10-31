import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import { StationDetailSheet } from './StationDetailSheet';
import type { StationPrice } from '../lib/radar';
import type { StationNode } from '../../../core/types/api';

/**
 * The station card is where a tapped pin turns into a price and an action, so the
 * things that must not regress are exactly those: the voucher price (two decimals,
 * comma), the saving against the pump, and a route CTA that stays honest when the
 * node has no coordinates.
 */

jest.mock('../../../core/i18n', () => ({
  useI18n: (selector: (s: any) => unknown) => selector({ t: (key: string) => key }),
}));

const station = {
  id: 'n1',
  brandId: 'brand-1',
  brandSlug: 'wog',
  name: 'WOG Lviv',
  city: 'Львів',
  address: 'Львів, вул. Стуса, 12',
  lat: 49.84,
  lng: 24.03,
} as StationNode;

function price(over: Partial<StationPrice> = {}): StationPrice {
  return { voucherPerLiter: 52.3, pumpPerLiter: 56, savingsPerLiter: 3.7, ...over };
}

function renderSheet(over: Partial<React.ComponentProps<typeof StationDetailSheet>> = {}) {
  const onClose = jest.fn();
  const onBuildRoute = jest.fn();
  render(
    <StationDetailSheet
      station={station}
      brandColor="#ff0000"
      price={price()}
      fuel="a-95"
      distanceKm={1.24}
      routeAvailable
      onClose={onClose}
      onBuildRoute={onBuildRoute}
      {...over}
    />,
  );
  return { onClose, onBuildRoute };
}

describe('StationDetailSheet — what a litre costs', () => {
  it('names the station and its address', () => {
    renderSheet();

    expect(screen.getByText('WOG Lviv')).toBeTruthy();
    expect(screen.getByText('Львів, вул. Стуса, 12')).toBeTruthy();
  });

  it('shows the voucher price in hryvnia with kopecks', () => {
    renderSheet();

    // Two decimals and a comma: `52,30`, never `52.30 ₴`.
    expect(screen.getByText('52,30')).toBeTruthy();
    expect(screen.getByText('/ map.litre')).toBeTruthy();
  });

  it('shows the saving against the pump price', () => {
    renderSheet();

    expect(screen.getByText('−3,70 map.vsPump')).toBeTruthy();
    expect(screen.getByText('map.pumpPrice 56,00 ₴')).toBeTruthy();
  });

  it('omits the saving when the voucher is not cheaper', () => {
    renderSheet({ price: price({ savingsPerLiter: 0 }) });

    expect(screen.queryByText(/map\.vsPump/)).toBeNull();
  });

  it('says so when the station has no price on this fuel', () => {
    renderSheet({ price: null });

    expect(screen.getByText('map.noPriceForFuel')).toBeTruthy();
    expect(screen.queryByText('52,30')).toBeNull();
  });

  it('labels the price block with the selected fuel', () => {
    renderSheet({ fuel: 'a-95' });

    expect(screen.getByText('map.voucherPrice · А95')).toBeTruthy();
  });
});

describe('StationDetailSheet — the route action', () => {
  it('builds a route when the station has coordinates', () => {
    const { onBuildRoute } = renderSheet();

    fireEvent.press(screen.getByRole('button', { name: 'map.buildRoute' }));
    expect(onBuildRoute).toHaveBeenCalledTimes(1);
  });

  it('disables the CTA rather than failing a tap when it has none', () => {
    const { onBuildRoute } = renderSheet({ routeAvailable: false });

    const cta = screen.getByRole('button', { name: 'map.routeUnavailable' });
    expect(cta).toBeDisabled();
    fireEvent.press(cta);
    expect(onBuildRoute).not.toHaveBeenCalled();
  });

  it('closes from the header control', () => {
    const { onClose } = renderSheet();

    fireEvent.press(screen.getByLabelText('common.close'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
