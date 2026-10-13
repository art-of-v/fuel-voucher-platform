import React from 'react';
import { View } from 'react-native';
import { render, screen } from '@testing-library/react-native';

import { PackageCard } from './PackageCard';
import { getTokens } from '../../../core/design/tokens';
import type { FuelPackage } from '../../../core/types/api';

/**
 * The term picker sits directly above the total it changes, separated by a hairline. When the chip
 * ladder wraps to a second row that row sat flush against the divider, because `summaryArea`'s
 * `paddingTop` spaces the divider from the РАЗОМ text BELOW it and nothing spaced it from the chips
 * above (#181). Nothing about the content changed, so the only thing worth pinning is that gap.
 */

jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

// The quote is fetched through apiClient, which pulls native modules Jest cannot load. It is also
// not what this file is about, so a real ladder is stubbed in rather than mocked all the way down —
// the picker only renders when there is one, and a picker-less card is the pre-#815 shape.
const mockLadder = {
  enabled: true,
  terms: ['1w', '2w', '1m', '2m', '3m'].map((term) => ({
    term,
    discountPerLiterUah: 3,
    pricePerLiterUah: 96.9,
    linePriceUah: 290.7,
    liters: 3,
    available: true,
  })),
};

jest.mock('../../vouchers/hooks/useTermQuote', () => ({
  useTermQuote: () => mockLadder,
}));

function mkPackage(overrides: Partial<FuelPackage> = {}): FuelPackage {
  return {
    id: 'p1',
    provider: 'OKKO',
    fuelTypeId: 'ft1',
    fuelName: 'ДП ЄВРО',
    liters: 3,
    pricePerLiter: 99.9,
    price: 299.7,
    discountPercent: 0,
    ...overrides,
  } as FuelPackage;
}

/** Every value of a (possibly nested) style array, flattened into one map. */
function flatten(style: unknown): Record<string, unknown> {
  if (Array.isArray(style)) return style.reduce((acc, s) => ({ ...acc, ...flatten(s) }), {});
  return (style ?? {}) as Record<string, unknown>;
}

describe('PackageCard total divider spacing (#181)', () => {
  it('keeps a gap between the term picker and the total divider', () => {
    const tokens = getTokens('lemberg');
    render(
      <PackageCard
        pkg={mkPackage()}
        index={0}
        quantity={1}
        isAdded={false}
        onAdd={jest.fn()}
        onQuantityChange={jest.fn()}
      />,
    );

    // The divider is the only view carrying a top border; the picker above it must not touch it.
    const divider = screen
      .UNSAFE_getAllByType(View)
      .map((node) => flatten(node.props.style))
      .find((style) => style.borderTopWidth === 1);

    expect(divider).toBeDefined();
    expect(divider!.marginTop).toBeGreaterThan(0);
    expect(divider!.marginTop).toBe(tokens.spacing.md);
  });

  it('spaces the divider from the picker by the same gap the picker takes for itself', () => {
    const tokens = getTokens('lemberg');
    render(
      <PackageCard
        pkg={mkPackage()}
        index={0}
        quantity={1}
        isAdded={false}
        term="1m"
        onAdd={jest.fn()}
        onTermChange={jest.fn()}
        onQuantityChange={jest.fn()}
      />,
    );

    // The picker is rendered, so the block below it is what the divider has to clear.
    expect(screen.getByText('term.short.1m')).toBeTruthy();

    const styles = screen.UNSAFE_getAllByType(View).map((node) => flatten(node.props.style));
    const divider = styles.find((style) => style.borderTopWidth === 1);

    // Symmetric spacing: the gap the picker takes above itself is the gap the divider takes below it,
    // so the block reads as one unit whether the ladder is one row or wraps to two.
    expect(divider!.marginTop).toBe(tokens.spacing.md);
  });
});

/**
 * A package with an empty shelf stays in the catalog — the customer can still read the price and walk
 * the app — but nothing about it can be bought. What matters is that this is visible ON the card:
 * a dead-looking shop is worse than a card that explains itself.
 */
describe('PackageCard out-of-stock presentation', () => {
  const renderCard = (overrides: Partial<FuelPackage> = {}) =>
    render(
      <PackageCard
        pkg={mkPackage(overrides)}
        index={0}
        quantity={1}
        isAdded={false}
        onAdd={jest.fn()}
        onQuantityChange={jest.fn()}
      />,
    );

  it('states the empty shelf on the card', () => {
    renderCard({ isAvailable: false });

    expect(screen.getAllByText('packages.outOfStock').length).toBeGreaterThan(0);
  });

  it('replaces the add-to-cart action with the same reason', () => {
    renderCard({ isAvailable: false });

    expect(screen.queryByText('packages.addToCart')).toBeNull();
    // Both the badge and the button say it, so the reason is never more than a glance away.
    expect(screen.getAllByText('packages.outOfStock').length).toBe(2);
  });

  it('withdraws the quantity stepper and the term ladder', () => {
    // Both change a price for something that cannot be bought. Leaving them live makes the card
    // invite a decision the customer cannot carry through.
    renderCard({ isAvailable: false });

    expect(screen.queryByText('packages.quantity')).toBeNull();
    expect(screen.queryByText('term.short.1m')).toBeNull();
  });

  it('dims the card', () => {
    renderCard({ isAvailable: false });

    // An empty shelf has to read as unavailable without a tap. The assertion carries its
    // own reason rather than a bare `toBeDefined()`, so a failure says what was lost.
    expect(
      screen
        .UNSAFE_getAllByType(View)
        .map((node) => flatten(node.props.style))
        .filter((style) => typeof style.opacity === 'number' && style.opacity < 1).length,
    ).toBeGreaterThan(0);
  });

  it('says nothing when the package is in stock', () => {
    renderCard({ isAvailable: true });

    expect(screen.queryByText('packages.outOfStock')).toBeNull();
    expect(screen.getByText('packages.addToCart')).toBeTruthy();
    expect(screen.getByText('packages.quantity')).toBeTruthy();
  });

  it('treats an older payload with no flag as buyable', () => {
    // The field is optional so a cached or older response degrades to "buyable" rather than to a shop
    // where nothing can be bought. The server still refuses the sale, so this fails loudly rather
    // than silently — the opposite trade-off (defaulting to unavailable) would be much worse.
    renderCard({ isAvailable: undefined });

    expect(screen.getByText('packages.addToCart')).toBeTruthy();
    expect(screen.queryByText('packages.outOfStock')).toBeNull();
  });
});
