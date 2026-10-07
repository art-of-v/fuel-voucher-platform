import React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react-native';

import { OrderCard } from './OrderCard';
import type { Order, Voucher } from '../../../core/types/api';

/**
 * An order card lists the vouchers it delivered, each one the *canonical* card - the
 * same component the wallet and every company stock section render.
 *
 * The suite exists because two `VoucherCard`s once existed: a second copy under
 * `src/components`, used only by `OrderCard`. The same voucher therefore looked
 * different inside an order than everywhere else. These assertions read the canonical
 * card's own vocabulary (`voucher.badge.blocked`, `codes.used`), which the old copy
 * never rendered, so a regression back to a private copy fails here.
 */

jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

// The swipe reveal reaches for the worklets native module and cannot load under Jest.
jest.mock('react-native-gesture-handler/ReanimatedSwipeable', () => {
  const React = require('react');
  const { View } = require('react-native');
  const Swipeable = React.forwardRef(function Swipeable(
    { children }: { children: React.ReactNode },
    ref: unknown,
  ) {
    return React.createElement(View, { ref }, children);
  });
  return { __esModule: true, default: Swipeable };
});

function mkVoucher(overrides: Partial<Voucher> = {}): Voucher {
  return {
    id: 'v1',
    provider: 'OKKO',
    fuelType: 'a95',
    amount: 10,
    status: 'active',
    legalEntityId: 'e1',
    workerUserId: null,
    ...overrides,
  } as Voucher;
}

function mkOrder(overrides: Partial<Order> = {}): Order {
  return {
    id: 'order-1',
    provider: 'OKKO',
    fuelType: 'a95',
    liters: 10,
    quantity: 1,
    price: 100,
    status: 'FULFILLED',
    createdAt: '2026-01-01T00:00:00Z',
    fulfilledAt: '2026-01-02T00:00:00Z',
    legalEntityId: 'e1',
    lineItems: [{ id: 'l1', provider: 'OKKO', fuelTypeId: 'a95', liters: 10, quantity: 1 }],
    ...overrides,
  } as Order;
}

beforeEach(() => {
  jest.useFakeTimers();
});

afterEach(() => {
  act(() => {
    jest.advanceTimersByTime(1000);
  });
  jest.useRealTimers();
});

function renderCard(
  vouchers: Voucher[],
  over: Partial<React.ComponentProps<typeof OrderCard>> = {},
) {
  const onVoucherPress = jest.fn();
  const onToggle = jest.fn();
  render(
    <OrderCard
      order={mkOrder({ vouchers } as Partial<Order>)}
      isExpanded
      onToggle={onToggle}
      onVoucherPress={onVoucherPress}
      brandColor="#ff0000"
      {...over}
    />,
  );
  return { onVoucherPress, onToggle };
}

describe('OrderCard - the vouchers it lists', () => {
  it('renders one card per voucher in the order', () => {
    renderCard([mkVoucher({ id: 'v1' }), mkVoucher({ id: 'v2', provider: 'Shell' })]);

    // The order header names its own provider too, so a brand appearing twice is
    // the signal that a card was rendered for it.
    expect(screen.getAllByText('OKKO').length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText('Shell').length).toBeGreaterThanOrEqual(1);
  });

  it('shows the canonical card, including the blocked state the old copy lacked', () => {
    renderCard([mkVoucher({ status: 'blocked' })]);

    // Only the canonical card badges a blocked voucher.
    expect(screen.getByText('voucher.badge.blocked')).toBeTruthy();
  });

  it('marks a used voucher as used', () => {
    renderCard([mkVoucher({ status: 'used' })]);

    expect(screen.getAllByText('codes.used').length).toBeGreaterThanOrEqual(1);
  });

  it('says so when the order delivered nothing yet', () => {
    renderCard([]);

    expect(screen.getByText('codes.noVouchersYet')).toBeTruthy();
  });
});

describe('OrderCard - tapping a listed voucher', () => {
  it('hands the tapped voucher to the screen', () => {
    const { onVoucherPress } = renderCard([mkVoucher({ id: 'v7' })]);

    fireEvent.press(screen.getAllByText('OKKO')[1]);

    // The screen opens the detail modal from this, so the voucher identity matters.
    expect(onVoucherPress).toHaveBeenCalledTimes(1);
    expect(onVoucherPress.mock.calls[0][0].id).toBe('v7');
  });
});
