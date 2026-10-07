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
