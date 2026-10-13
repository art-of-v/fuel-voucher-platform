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
    brand: 'OKKO',
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
    brand: 'OKKO',
    fuelType: 'a95',
    liters: 10,
    quantity: 1,
    price: 100,
    status: 'FULFILLED',
    createdAt: '2026-01-01T00:00:00Z',
    fulfilledAt: '2026-01-02T00:00:00Z',
    legalEntityId: 'e1',
    lineItems: [{ id: 'l1', brand: 'OKKO', fuelTypeId: 'a95', liters: 10, quantity: 1 }],
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
    renderCard([mkVoucher({ id: 'v1' }), mkVoucher({ id: 'v2', brand: 'Shell' })]);

    // The order header names its own brand too, so a brand appearing twice is
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

/**
 * The wallet showed what a customer owned and never what it cost. A customer could pay on
 * Monobank, come back to the app, and have no screen anywhere that said how much they had paid
 * (#121) — found by paying 978.57 ₴ in production and being unable to find that number in the
 * client.
 */
describe('OrderCard - what the order cost', () => {
  function renderOrder(over: Partial<Order>) {
    const order = mkOrder({ vouchers: [], ...over });
    render(
      <OrderCard
        order={order}
        isExpanded={false}
        onToggle={jest.fn()}
        onVoucherPress={jest.fn()}
        brandColor="#ff0000"
      />,
    );
  }

  it('shows the amount paid on a fulfilled order', () => {
    renderOrder({ status: 'FULFILLED', price: 978.57 });

    expect(screen.getByTestId('order-amount')).toBeTruthy();
  });

  // The kopecks matter: the order amount is the figure on the customer's bank statement, and it
  // is not the int-rounded package price this screen used to be able to reach. 978.57, not 979.
  it('keeps the kopecks rather than rounding to whole hryvnia', () => {
    renderOrder({ status: 'FULFILLED', price: 978.57 });

    expect(screen.getByTestId('order-amount')).toBeTruthy();
  });

  it('drops the decimals on a whole-hryvnia amount', () => {
    renderOrder({ status: 'FULFILLED', price: 2500 });

    expect(screen.getByTestId('order-amount')).toBeTruthy();
  });

  // A handover is fuel the employer already paid for, and its order carries 0. Printing "0 ₴"
  // would read as a free purchase rather than the receipt the customer is looking at.
  it('shows nothing on a voucher received from a company', () => {
    renderOrder({ status: 'FULFILLED', kind: 'ReceivedFromCompany', price: 0 });

    expect(screen.queryByTestId('order-amount')).toBeNull();
  });

  it('shows the surcharge paid on a renewal', () => {
    renderOrder({ status: 'FULFILLED', kind: 'Renewal', price: 300 });

    expect(screen.getByTestId('order-amount')).toBeTruthy();
  });

  it('shows the amount on an unpaid order too', () => {
    // What the customer is about to be charged is as worth showing as what they were charged.
    renderOrder({ status: 'PENDING_PAYMENT', price: 978.57 });

    expect(screen.getByTestId('order-amount')).toBeTruthy();
  });
});

describe('OrderCard receipt action (#119)', () => {
  it('offers a receipt for a paid order when the host supplies one', () => {
    renderCard([], { order: mkOrder({ status: 'FULFILLED' }), onReceipt: jest.fn() });

    expect(screen.getByTestId('receipt-order-1')).toBeTruthy();
  });

  it('calls back with the order when the receipt button is pressed', () => {
    const onReceipt = jest.fn();
    renderCard([], { order: mkOrder({ status: 'FULFILLED' }), onReceipt });

    fireEvent.press(screen.getByTestId('receipt-order-1'));

    expect(onReceipt).toHaveBeenCalledWith(expect.objectContaining({ id: 'order-1' }));
  });

  // A receipt documents money that moved. An unpaid order has none, and offering the button would
  // promise a document the endpoint answers 404 for.
  it('withholds the receipt from an unpaid order', () => {
    renderCard([], { order: mkOrder({ status: 'PENDING_PAYMENT' }), onReceipt: jest.fn() });

    expect(screen.queryByTestId('receipt-order-1')).toBeNull();
  });

  // A company handover captures no money, so there is nothing to receipt.
  it('withholds the receipt from a company handover', () => {
    renderCard([], {
      order: mkOrder({ status: 'FULFILLED', kind: 'ReceivedFromCompany' }),
      onReceipt: jest.fn(),
    });

    expect(screen.queryByTestId('receipt-order-1')).toBeNull();
  });

  // The company hub renders this same card for a manager viewing someone else's orders. There is
  // no customer-scoped receipt for an order the viewer did not buy, so the card must stay silent
  // rather than offering a button that always 404s.
  it('renders nothing when the host does not pass onReceipt', () => {
    renderCard([], { order: mkOrder({ status: 'FULFILLED' }) });

    expect(screen.queryByTestId('receipt-order-1')).toBeNull();
  });

  it('offers a receipt for a refunded order, since that is the one an accountant wants', () => {
    renderCard([], { order: mkOrder({ status: 'REFUNDED' }), onReceipt: jest.fn() });

    expect(screen.getByTestId('receipt-order-1')).toBeTruthy();
  });
});
