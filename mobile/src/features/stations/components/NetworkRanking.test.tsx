import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import { NetworkRankingList } from './NetworkRanking';
import type { BrandRank, RankedStation, StationPrice } from '../lib/radar';
import type { StationNode } from '../../../core/types/api';

/**
 * The ranking is the map's answer to "who is cheapest near me", and its rows are
 * where the old screen failed visibly: the "cheapest" badge shared a flex row with
 * the network name, wrapped onto it, and the price was rendered with `toFixed(2)`
 * — a dot, in a product whose money always uses a comma.
 */

jest.mock('../../../core/i18n', () => ({
  useI18n: (selector: (s: any) => unknown) => selector({ t: (key: string) => key }),
}));

function node(id: string, name: string): StationNode {
  return {
    id,
    brandId: 'brand-1',
    brandSlug: 'wog',
    name,
    city: 'Львів',
    address: 'Львів, вул. Стуса, 12',
    lat: 49.84,
    lng: 24.03,
  } as StationNode;
}

function price(over: Partial<StationPrice> = {}): StationPrice {
  return { voucherPerLiter: 52.3, pumpPerLiter: 56, savingsPerLiter: 3.7, ...over };
}

function ranked(entry: StationNode, distanceKm: number | null = 1.24): RankedStation {
  return { node: entry, price: price(), distanceKm };
}

function brand(over: Partial<BrandRank> = {}): BrandRank {
  return {
    brandId: 'brand-1',
    brandSlug: 'wog',
    price: price(),
    nearestDistanceKm: 1.24,
    nodes: [ranked(node('n1', 'WOG Lviv'))],
    inRange: true,
    ...over,
  };
}

function renderList(
  brands: BrandRank[],
  over: Partial<React.ComponentProps<typeof NetworkRankingList>> = {},
) {
  const onSelectBrand = jest.fn();
  const onSelectStation = jest.fn();
  render(
    <NetworkRankingList
      brands={brands}
      activeBrand={null}
      onSelectBrand={onSelectBrand}
      onSelectStation={onSelectStation}
      {...over}
    />,
  );
  return { onSelectBrand, onSelectStation };
}

describe('NetworkRankingList — a network row', () => {
  it('renders the price with a decimal comma and the per-litre unit', () => {
    renderList([brand()]);

    expect(screen.getByText('52,30')).toBeTruthy();
    expect(screen.getByText('map.perLiter')).toBeTruthy();
  });

  it('shows the saving against the pump price', () => {
    renderList([brand()]);

    expect(screen.getByText(/3,70/)).toBeTruthy();
    expect(screen.getByText(/map\.vsPump/)).toBeTruthy();
  });

  it('summarises the nearest distance and the station count on one line', () => {
    // Both facts at a glance, and the distance uses the same comma as the price.
    renderList([brand()]);

    expect(screen.getByText('1,2 map.km · 1 map.stationsShort')).toBeTruthy();
  });

  it('falls back to the count alone when there is no location', () => {
    renderList([brand({ nearestDistanceKm: null })]);

    expect(screen.getByText('1 map.stationsShort')).toBeTruthy();
  });

  it('marks only the first reachable network as the cheapest', () => {
    renderList([
      brand(),
      brand({ brandId: 'brand-2', brandSlug: 'okko', price: price({ voucherPerLiter: 53.4 }) }),
    ]);

    expect(screen.getAllByText('map.cheapest')).toHaveLength(1);
  });

  it('does not call an out-of-range network the cheapest', () => {
    // An unreachable network can be the cheapest number on the sheet; it must not
    // wear the badge, or the first row reads as "go here" when there is no pump.
    renderList([brand({ inRange: false, nearestDistanceKm: 75 })]);

    expect(screen.queryByText('map.cheapest')).toBeNull();
  });

  it('says an out-of-range network is out of range instead of its station count', () => {
    renderList([brand({ inRange: false, nearestDistanceKm: 75 })]);

    expect(screen.getByText('75 map.km · map.outOfRange')).toBeTruthy();
  });

  it('opens the drill-in for the network that was tapped', () => {
    const { onSelectBrand } = renderList([brand()]);

    fireEvent.press(screen.getByText('WOG'));

    expect(onSelectBrand).toHaveBeenCalledWith('brand-1');
  });
});

describe('NetworkRankingList — one network’s stations', () => {
  it('lists each station with its short address and distance', () => {
    renderList([], { activeBrand: brand() });

    expect(screen.getByText('WOG Lviv')).toBeTruthy();
    expect(screen.getByText('Львів, вул. Стуса, 12')).toBeTruthy();
    expect(screen.getByText('1,2 map.km')).toBeTruthy();
  });

  it('hands the tapped station back with its node', () => {
    const { onSelectStation } = renderList([], { activeBrand: brand() });

    fireEvent.press(screen.getByText('WOG Lviv'));

    expect(onSelectStation).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'n1', name: 'WOG Lviv' }),
    );
  });
});

describe('NetworkRankingList — nothing to rank', () => {
  it('explains the empty list instead of rendering a blank sheet', () => {
    renderList([]);

    expect(screen.getByText('map.noOffersTitle')).toBeTruthy();
    expect(screen.getByText('map.noOffersDescription')).toBeTruthy();
  });
});
