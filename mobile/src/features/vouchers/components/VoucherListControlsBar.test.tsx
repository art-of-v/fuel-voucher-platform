import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';

import { VoucherListControlsBar } from './VoucherListControlsBar';
import { EMPTY_FILTERS, type ListControls } from '../lib/listControls';
import type { Voucher } from '../../../core/types/api';

/**
 * The filter/sort bar over the worker's own list (#161 H0, mounted by #150's W3 follow-up).
 *
 * The logic lives in `listControls.ts` and is tested there. What this suite pins is the bar
 * itself: which controls it offers, what it tells the worker about how much is hidden, and that
 * a tap reports the new state instead of applying it silently.
 *
 * The count is the point of a filter: without it a worker cannot tell a filtered list from an
 * empty one, so it is asserted rather than assumed.
 */

jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

function voucher(overrides: Partial<Voucher> = {}): Voucher {
  return {
    id: 'v1',
    brand: 'OKKO',
    fuelType: 'a95',
    amount: 10,
    status: 'active',
    expirationDate: '2026-12-31T00:00:00Z',
    legalEntityId: null,
    workerUserId: 'w1',
    ...overrides,
  } as Voucher;
}

const controls = (overrides: Partial<ListControls> = {}): ListControls => ({
  sortKey: 'expiry',
  sortDirection: 'asc',
  filters: { ...EMPTY_FILTERS },
  ...overrides,
});

function renderBar(
  vouchers: Voucher[] = [voucher()],
  state: ListControls = controls(),
  onChange = jest.fn(),
) {
  render(
    <VoucherListControlsBar
      vouchers={vouchers}
      controls={state}
      onChange={onChange}
      countLabel="codes.stock.issuedToYou"
    />,
  );
  return { onChange };
}

describe('VoucherListControlsBar', () => {
  it('offers a sort for every axis', () => {
    renderBar();

    expect(screen.getByTestId('sort-expiry')).toBeTruthy();
    expect(screen.getByTestId('sort-brand')).toBeTruthy();
    expect(screen.getByTestId('sort-fuel')).toBeTruthy();
    expect(screen.getByTestId('sort-liters')).toBeTruthy();
  });

  it('offers a filter per brand and fuel that is actually in the list', () => {
    renderBar([voucher({ id: 'v1' }), voucher({ id: 'v2', brand: 'WOG', fuelType: 'diesel' })]);

    expect(screen.getByTestId('filter-brand-OKKO')).toBeTruthy();
    expect(screen.getByTestId('filter-brand-WOG')).toBeTruthy();
    expect(screen.getByTestId('filter-fuel-a95')).toBeTruthy();
    expect(screen.getByTestId('filter-fuel-diesel')).toBeTruthy();
  });

  it('says how much of the list is shown', () => {
    renderBar([voucher({ id: 'v1' }), voucher({ id: 'v2' })]);

    expect(screen.getByText('codes.stock.issuedToYou 2/2')).toBeTruthy();
  });

  it('says how many are hidden by the filter', () => {
    renderBar([voucher({ id: 'v1' }), voucher({ id: 'v2', brand: 'WOG' })], {
      ...controls(),
      filters: { ...EMPTY_FILTERS, brand: 'OKKO' },
    });

    // Without this a worker cannot tell a filtered list from an empty one.
    expect(screen.getByText(/1 voucherList\.hidden/)).toBeTruthy();
  });

  it('offers to clear the filters only while one is on', () => {
    const { rerender } = render(
      <VoucherListControlsBar vouchers={[voucher()]} controls={controls()} onChange={jest.fn()} />,
    );
    expect(screen.queryByTestId('clear-filters')).toBeNull();

    rerender(
      <VoucherListControlsBar
        vouchers={[voucher()]}
        controls={controls({ filters: { ...EMPTY_FILTERS, hideExpired: true } })}
        onChange={jest.fn()}
      />,
    );
    expect(screen.getByTestId('clear-filters')).toBeTruthy();
  });

  it('reports the new sort when a sort chip is pressed', () => {
    const { onChange } = renderBar();

    fireEvent.press(screen.getByTestId('sort-liters'));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ sortKey: 'liters', sortDirection: 'desc' }),
    );
  });

  it('reports the brand filter when a brand chip is pressed', () => {
    const { onChange } = renderBar([voucher({ id: 'v1' }), voucher({ id: 'v2', brand: 'WOG' })]);

    fireEvent.press(screen.getByTestId('filter-brand-WOG'));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ filters: expect.objectContaining({ brand: 'WOG' }) }),
    );
  });

  it('reports the not-expired filter when it is pressed', () => {
    const { onChange } = renderBar();

    fireEvent.press(screen.getByTestId('filter-not-expired'));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ filters: expect.objectContaining({ hideExpired: true }) }),
    );
  });
});
